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

                // --- Кодировка ---
                var charset = response.Content.Headers.ContentType?.CharSet ?? "utf-8";
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Encoding sourceEncoding;
                try { sourceEncoding = Encoding.GetEncoding(charset); }
                catch { sourceEncoding = Encoding.UTF8; }

                string html;
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream, sourceEncoding))
                    html = await reader.ReadToEndAsync();

                // --- Обработка HTML ---
                if (contentType != null && contentType.Contains("text/html"))
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var head = doc.DocumentNode.SelectSingleNode("//head");
                    if (head != null)
                    {
                        // Удаляем старые <meta charset> и content-type
                        var oldMetas = head.SelectNodes(".//meta[@charset]") ?? new HtmlNodeCollection(null);
                        foreach (var m in oldMetas) m.Remove();

                        var httpEquivMetas = head.SelectNodes(".//meta[translate(@http-equiv,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='content-type']") ?? new HtmlNodeCollection(null);
                        foreach (var m in httpEquivMetas) m.Remove();

                        // ✅ Добавляем новый meta charset
                        var metaCharset = doc.CreateElement("meta");
                        metaCharset.SetAttributeValue("charset", "utf-8");
                        head.PrependChild(metaCharset);

                        // ✅ Меняем base на твой API, а не на чужой сайт
                        var baseTag = doc.CreateElement("base");
                        baseTag.SetAttributeValue("href", "/api/proxy/web/");
                        head.PrependChild(baseTag);
                    }

                    // --- Внедрение JS-переопределения ---
                    var jsCode = @"
                    (function() {
                        const origFetch = window.fetch;
                        window.fetch = function(url, opts) {
                            if (url.startsWith('https://galaxy.mobstudio.ru/')) {
                                url = url.replace('https://galaxy.mobstudio.ru/', '/api/proxy/');
                            } else if (url.startsWith('/web/')) {
                                url = '/api/proxy' + url;
                            }
                            return origFetch(url, opts);
                        };

                        const origOpen = XMLHttpRequest.prototype.open;
                        XMLHttpRequest.prototype.open = function(method, url) {
                            if (url.startsWith('https://galaxy.mobstudio.ru/')) {
                                url = url.replace('https://galaxy.mobstudio.ru/', '/api/proxy/');
                            } else if (url.startsWith('/web/')) {
                                url = '/api/proxy' + url;
                            }
                            return origOpen.apply(this, [method, url]);
                        };
                        console.log('✅ GalaxyProxy: JS интерцептор активен');
                    })();
                ";

                    var proxyScript = HtmlNode.CreateNode($"<script>{jsCode}</script>");
                    var body = doc.DocumentNode.SelectSingleNode("//body");
                    if (body != null)
                    {
                        body.AppendChild(proxyScript);
                    }

                    var modifiedHtml = doc.DocumentNode.OuterHtml;
                    return Content(modifiedHtml, "text/html; charset=utf-8", Encoding.UTF8);
                }
                else
                {
                    // --- Всё остальное (css, js, png и т.п.) ---
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
    }

}