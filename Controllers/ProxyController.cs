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

        [HttpGet]
        [Route("{*path}")]
        public async Task<IActionResult> Get(string path = "")
        {
            var client = _httpClientFactory.CreateClient();
            // Если путь пустой, используем базовый URL, иначе конструируем полный URL
            var targetUrl = string.IsNullOrEmpty(path) ? new Uri(TargetBaseUrl) : new Uri(new Uri(TargetBaseUrl), path);

            try
            {
                var response = await client.GetAsync(targetUrl);

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
                }

                var contentType = response.Content.Headers.ContentType?.ToString();

                if (contentType != null && contentType.Contains("text/html"))
                {
                    var html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    // 1. Агрессивное удаление Service Worker
                    var scriptNodes = doc.DocumentNode.SelectNodes("//script");
                    if (scriptNodes != null)
                    {
                        foreach (var script in scriptNodes.ToList())
                        {
                            // Проверяем и встроенный код, и внешние ссылки
                            var src = script.GetAttributeValue("src", string.Empty);
                            if (script.InnerHtml.Contains("serviceWorker.register") || src.Contains("sw.js") || src.Contains("service-worker"))
                            {
                                script.Remove();
                                _logger.LogInformation("Удален скрипт Service Worker.");
                            }
                        }
                    }

                    // 2. Внедряем <base> тег
                    var head = doc.DocumentNode.SelectSingleNode("//head");
                    if (head != null)
                    {
                        var baseTag = doc.CreateElement("base");
                        baseTag.SetAttributeValue("href", TargetBaseUrl);
                        head.PrependChild(baseTag);

                        var metaCharset = doc.CreateElement("meta");
                        metaCharset.SetAttributeValue("charset", "utf-8");
                        head.PrependChild(metaCharset);
                    }

                    // 3. Внедряем наш кастомный скрипт
                    var body = doc.DocumentNode.SelectSingleNode("//body");
                    if (body != null)
                    {
                        // Найдите основной скрипт
                        var mainScript = doc.DocumentNode.SelectSingleNode("//script[@src]");
                        var testScript = doc.CreateElement("script");
                        var jsCode = @"(function() {
    const proxyPrefix = '/api/proxy';
    // fetch
    const origFetch = window.fetch;
    window.fetch = function(input, init) {
        let url = typeof input === 'string' ? input : input.url;
        if (url.startsWith('/') || url.startsWith('https://galaxy.mobstudio.ru')) {
            let newUrl = proxyPrefix + (url.startsWith('/') ? url : url.replace('https://galaxy.mobstudio.ru', ''));
            if (typeof input === 'string') input = newUrl;
            else input.url = newUrl;
        }
        return origFetch(input, init);
    };
    // XMLHttpRequest
    const origOpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(method, url, ...args) {
        if (url.startsWith('/') || url.startsWith('https://galaxy.mobstudio.ru')) {
            url = proxyPrefix + (url.startsWith('/') ? url : url.replace('https://galaxy.mobstudio.ru', ''));
        }
        return origOpen.call(this, method, url, ...args);
    };
})();";
                        testScript.InnerHtml = jsCode;

                        if (mainScript != null)
                        {
                            mainScript.ParentNode.InsertBefore(testScript, mainScript);
                        }
                        else
                        {
                            // fallback: добавьте в конец body
                            body.AppendChild(testScript);
                        }
                    }

                    var modifiedHtml = doc.DocumentNode.OuterHtml;
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
                _logger.LogError(ex, "Ошибка при проксировании запроса на {Url}", targetUrl);
                return StatusCode(500, "Внутренняя ошибка сервера при проксировании запроса.");
            }
        }

        [HttpPost]
        [Route("{*path}")]
        public async Task<IActionResult> Post(string path = "")
        {
            var client = _httpClientFactory.CreateClient();
            var targetUrl = string.IsNullOrEmpty(path) ? new Uri(TargetBaseUrl) : new Uri(new Uri(TargetBaseUrl), path);

            var content = new StreamContent(Request.Body);
            foreach (var header in Request.Headers)
            {
                if (!header.Key.StartsWith(":"))
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            var response = await client.PostAsync(targetUrl, content);
            var responseBytes = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.ToString();

            return new FileContentResult(responseBytes, contentType ?? "application/octet-stream");
        }
    }
}