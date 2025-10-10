using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ProxyController> _logger;
        private const string TargetBaseUrl = "https://galaxy.mobstudio.ru/web/";

        public ProxyController(IHttpClientFactory httpClientFactory, ILogger<ProxyController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // Добавляем заголовки для Galaxy
        private void AddGalaxyHeaders(HttpRequestMessage request)
        {
            request.Headers.Add("x-galaxy-client-ver", "9.5");
            request.Headers.Add("x-galaxy-kbv", "352");
            request.Headers.Add("x-galaxy-lng", "ru");
            request.Headers.Add("x-galaxy-model", "chrome 140.0.0.0");
            request.Headers.Add("x-galaxy-orientation", "portrait");
            request.Headers.Add("x-galaxy-os-ver", "1");
            request.Headers.Add("x-galaxy-platform", "web");
            request.Headers.Add("x-galaxy-scr-dpi", "1");
            request.Headers.Add("x-galaxy-scr-h", "945");
            request.Headers.Add("x-galaxy-scr-w", "700");
            request.Headers.Add("x-galaxy-user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
        }

        [HttpGet]
        [Route("{*path}")]
        public async Task<IActionResult> Get(string path = "")
        {
            var client = _httpClientFactory.CreateClient();
            var targetUrl = string.IsNullOrEmpty(path)
                ? new Uri(TargetBaseUrl)
                : new Uri(new Uri(TargetBaseUrl), path);

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                AddGalaxyHeaders(request);

                var response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode,
                        await response.Content.ReadAsStringAsync());
                }

                var contentType = response.Content.Headers.ContentType?.ToString();

                // ---------- Фикс кодировки ----------
                var charset = response.Content.Headers.ContentType?.CharSet ?? "utf-8";

                // Поддержка legacy-кодировок (windows-1251 и т.п.)
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                Encoding sourceEncoding;
                try
                {
                    sourceEncoding = Encoding.GetEncoding(charset);
                }
                catch
                {
                    sourceEncoding = Encoding.UTF8; // fallback
                }

                string html;
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream, sourceEncoding))
                {
                    html = await reader.ReadToEndAsync();
                }
                // ------------------------------------

                if (contentType != null && contentType.Contains("text/html"))
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    // Удаляем Service Worker
                    var scriptNodes = doc.DocumentNode.SelectNodes("//script");
                    if (scriptNodes != null)
                    {
                        foreach (var script in scriptNodes.ToList())
                        {
                            var src = script.GetAttributeValue("src", string.Empty);
                            if (script.InnerHtml.Contains("serviceWorker.register") ||
                                src.Contains("sw.js") ||
                                src.Contains("service-worker"))
                            {
                                script.Remove();
                            }
                        }
                    }

                    // Вставляем <base> и принудительно <meta charset="utf-8">
                    var head = doc.DocumentNode.SelectSingleNode("//head");
                    if (head != null)
                    {
                        // Удаляем старые <meta charset> если были
                        var oldMetas = head.SelectNodes(".//meta[@charset]");
                        if (oldMetas != null)
                        {
                            foreach (var m in oldMetas) m.Remove();
                        }

                        // Также удаляем <meta http-equiv="Content-Type">
                        var httpEquivMetas = head.SelectNodes(".//meta[translate(@http-equiv,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='content-type']");
                        if (httpEquivMetas != null)
                        {
                            foreach (var m in httpEquivMetas) m.Remove();
                        }

                        // Добавляем новый meta charset=utf-8
                        var metaCharset = doc.CreateElement("meta");
                        metaCharset.SetAttributeValue("charset", "utf-8");
                        head.PrependChild(metaCharset);

                        // Добавляем <base href="...">
                        var baseTag = doc.CreateElement("base");
                        baseTag.SetAttributeValue("href", TargetBaseUrl);
                        head.PrependChild(baseTag);
                    }

                    // Внедряем JS (через base64, чтобы кириллица не ломалась)
                    var jsCode = "alert('Привет! Это прокси-скрипт 🚀');";
                    var jsBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsCode));

                    var proxyScript = doc.CreateElement("script");
                    proxyScript.InnerHtml = $"eval(atob('{jsBase64}'));";

                    var body = doc.DocumentNode.SelectSingleNode("//body");
                    if (body != null)
                    {
                        var mainScript = doc.DocumentNode.SelectSingleNode("//script[@src]");
                        if (mainScript != null)
                            mainScript.ParentNode.InsertBefore(proxyScript, mainScript);
                        else
                            body.AppendChild(proxyScript);
                    }

                    var modifiedHtml = doc.DocumentNode.OuterHtml;

                    // ⚙️ Возвращаем как UTF-8, независимо от исходной кодировки
                    return Content(modifiedHtml, "text/html; charset=utf-8", Encoding.UTF8);
                }
                else
                {
                    var content = await response.Content.ReadAsByteArrayAsync();
                    return new FileContentResult(content, contentType ?? "application/octet-stream");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проксировании GET {Url}", targetUrl);
                return StatusCode(500, "Внутренняя ошибка сервера при проксировании GET.");
            }
        }



        [HttpPost]
        [Route("{*path}")]
        public async Task<IActionResult> Post(string path = "")
        {
            var client = _httpClientFactory.CreateClient();
            var targetUrl = string.IsNullOrEmpty(path) ? new Uri(TargetBaseUrl) : new Uri(new Uri(TargetBaseUrl), path);

            var requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
            var contentType = Request.ContentType ?? "application/x-www-form-urlencoded";
            var content = new StringContent(requestBody, Encoding.UTF8, contentType);

            var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
            {
                Content = content
            };
            AddGalaxyHeaders(request);

            try
            {
                var response = await client.SendAsync(request);
                var responseBytes = await response.Content.ReadAsByteArrayAsync();
                var respContentType = response.Content.Headers.ContentType?.ToString();

                return new FileContentResult(responseBytes, respContentType ?? "application/octet-stream");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проксировании POST {Url}", targetUrl);
                return StatusCode(500, "Внутренняя ошибка сервера при проксировании POST.");
            }
        }
    }
}