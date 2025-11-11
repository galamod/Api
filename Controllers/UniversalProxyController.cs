using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Api.Controllers
{
    /// <summary>
    /// Универсальный прокси-контроллер для проксирования GET и POST запросов
    /// с копированием всех заголовков от клиента
    /// </summary>
    [ApiController]
    [Route("api/universal-proxy")]
    public class UniversalProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<UniversalProxyController> _logger;

        public UniversalProxyController(
            IHttpClientFactory httpClientFactory,
            ILogger<UniversalProxyController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// Проксирует GET запрос к целевому URL
        /// </summary>
        /// <param name="targetUrl">Целевой URL для проксирования</param>
        [HttpGet]
        public async Task<IActionResult> ProxyGet([FromQuery] string targetUrl)
        {
            if (string.IsNullOrWhiteSpace(targetUrl))
                return BadRequest(new { error = "targetUrl parameter is required" });

            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
                return BadRequest(new { error = "Invalid URL format" });

            try
            {
                var client = _httpClientFactory.CreateClient("GalaxyClient");
                var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);

                // Копируем все заголовки из входящего запроса
                CopyRequestHeaders(request, targetUrl);

                _logger.LogInformation("Proxying GET request to: {Url}", targetUrl);
                _logger.LogDebug("Request headers: {Headers}", 
                    string.Join(", ", request.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}")));

                var response = await client.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("Response status: {StatusCode}, Content-Type: {ContentType}", 
                    response.StatusCode, 
                    response.Content.Headers.ContentType?.ToString());

                // Возвращаем ответ с оригинальным статус-кодом
                return new ContentResult
                {
                    Content = content,
                    ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request error while proxying GET to {Url}", targetUrl);
                return StatusCode(502, new { error = "Proxy error", message = ex.Message, url = targetUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while proxying GET to {Url}", targetUrl);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        /// <summary>
        /// Проксирует POST запрос к целевому URL
        /// </summary>
        /// <param name="targetUrl">Целевой URL для проксирования</param>
        [HttpPost]
        public async Task<IActionResult> ProxyPost([FromQuery] string targetUrl)
        {
            if (string.IsNullOrWhiteSpace(targetUrl))
                return BadRequest(new { error = "targetUrl parameter is required" });

            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
                return BadRequest(new { error = "Invalid URL format" });

            try
            {
                var client = _httpClientFactory.CreateClient("GalaxyClient");
                var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);

                // Копируем все заголовки из входящего запроса
                CopyRequestHeaders(request, targetUrl);

                // Читаем и копируем тело запроса
                if (Request.ContentLength > 0 || Request.Body.CanRead)
                {
                    Request.EnableBuffering();
                    Request.Body.Position = 0;

                    using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
                    var body = await reader.ReadToEndAsync();
                    Request.Body.Position = 0;

                    if (!string.IsNullOrEmpty(body))
                    {
                        var contentType = Request.ContentType ?? "application/json";
                        request.Content = new StringContent(body, Encoding.UTF8, contentType);

                        _logger.LogDebug("Request body: {Body}", 
                            body.Length > 500 ? body.Substring(0, 500) + "..." : body);
                    }
                }

                _logger.LogInformation("Proxying POST request to: {Url}", targetUrl);
                _logger.LogDebug("Request headers: {Headers}", 
                    string.Join(", ", request.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}")));

                var response = await client.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("Response status: {StatusCode}, Content-Type: {ContentType}", 
                    response.StatusCode, 
                    response.Content.Headers.ContentType?.ToString());

                // Возвращаем ответ с оригинальным статус-кодом
                return new ContentResult
                {
                    Content = content,
                    ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request error while proxying POST to {Url}", targetUrl);
                return StatusCode(502, new { error = "Proxy error", message = ex.Message, url = targetUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while proxying POST to {Url}", targetUrl);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        /// <summary>
        /// Копирует заголовки из входящего запроса в исходящий
        /// </summary>
        private void CopyRequestHeaders(HttpRequestMessage request, string targetUrl)
        {
            // Список заголовков, которые НЕЛЬЗЯ копировать (управляются автоматически)
            var forbiddenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Host",
                "Content-Length",  // ← КРИТИЧНО! Автоматически управляется HttpContent
                "Content-Type",    // ← Управляется HttpContent
                "Transfer-Encoding",
                "Connection",
                "Upgrade",
                "Expect"
            };

            // 1. КРИТИЧНЫЕ ЗАГОЛОВКИ - копируем в первую очередь

            // Cookie (ОЧЕНЬ ВАЖНО для аутентификации)
            if (Request.Headers.TryGetValue("Cookie", out var cookies))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookies.ToString());
                _logger.LogDebug("Copied Cookie header");
            }

            // Authorization
            if (Request.Headers.TryGetValue("Authorization", out var auth))
            {
                request.Headers.TryAddWithoutValidation("Authorization", auth.ToString());
                _logger.LogDebug("Copied Authorization header");
            }

            // X-Requested-With (для AJAX запросов)
            if (Request.Headers.TryGetValue("X-Requested-With", out var xRequestedWith))
            {
                request.Headers.TryAddWithoutValidation("X-Requested-With", xRequestedWith.ToString());
            }

            // 2. СТАНДАРТНЫЕ БРАУЗЕРНЫЕ ЗАГОЛОВКИ
            
            // Accept
            if (Request.Headers.TryGetValue("Accept", out var accept))
            {
                request.Headers.TryAddWithoutValidation("Accept", accept.ToString());
            }
            else
            {
                request.Headers.TryAddWithoutValidation("Accept", 
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            }

            // Accept-Language
            if (Request.Headers.TryGetValue("Accept-Language", out var acceptLang))
            {
                request.Headers.TryAddWithoutValidation("Accept-Language", acceptLang.ToString());
            }
            else
            {
                request.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            }

            // Accept-Encoding
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");

            // Cache-Control
            if (Request.Headers.TryGetValue("Cache-Control", out var cacheControl))
            {
                request.Headers.TryAddWithoutValidation("Cache-Control", cacheControl.ToString());
            }
            else
            {
                request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
                request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
            }

            // 3. ORIGIN И REFERER
            
            // Origin
            if (Request.Headers.TryGetValue("Origin", out var origin))
            {
                request.Headers.TryAddWithoutValidation("Origin", origin.ToString());
                _logger.LogDebug("Copied Origin: {Origin}", origin);
            }

            // Referer
            if (Request.Headers.TryGetValue("Referer", out var referer))
            {
                request.Headers.TryAddWithoutValidation("Referer", referer.ToString());
                _logger.LogDebug("Copied Referer: {Referer}", referer);
            }

            // 4. SEC-FETCH ЗАГОЛОВКИ (важны для CORS)
            
            if (Request.Headers.TryGetValue("Sec-Fetch-Dest", out var secFetchDest))
            {
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", secFetchDest.ToString());
            }

            if (Request.Headers.TryGetValue("Sec-Fetch-Mode", out var secFetchMode))
            {
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", secFetchMode.ToString());
            }

            if (Request.Headers.TryGetValue("Sec-Fetch-Site", out var secFetchSite))
            {
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", secFetchSite.ToString());
            }

            // 5. USER-AGENT
            
            if (Request.Headers.TryGetValue("User-Agent", out var userAgent))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent.ToString());
            }
            else
            {
                request.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
            }

            // 6. GALAXY-СПЕЦИФИЧНЫЕ ЗАГОЛОВКИ (если есть)
            
            var galaxyHeaders = new[]
            {
                "x-galaxy-client-ver", "x-galaxy-kbv", "x-galaxy-lng",
                "x-galaxy-model", "x-galaxy-orientation", "x-galaxy-os-ver",
                "x-galaxy-platform", "x-galaxy-scr-dpi", "x-galaxy-scr-h",
                "x-galaxy-scr-w", "x-galaxy-user-agent"
            };

            foreach (var header in galaxyHeaders)
            {
                if (Request.Headers.TryGetValue(header, out var value))
                {
                    request.Headers.TryAddWithoutValidation(header, value.ToString());
                }
            }

            // 7. ВСЕ ОСТАЛЬНЫЕ CUSTOM ЗАГОЛОВКИ (X-*, Content-Type и т.д.)

            foreach (var header in Request.Headers)
            {
                var headerName = header.Key;

                // Пропускаем запрещённые заголовки
                if (forbiddenHeaders.Contains(headerName) ||
                    request.Headers.Contains(headerName))
                {
                    continue;
                }

                // Копируем безопасные заголовки
                request.Headers.TryAddWithoutValidation(headerName, header.Value.ToString());
            }

            _logger.LogDebug("Total headers copied: {Count}", request.Headers.Count());
        }
    }
}
