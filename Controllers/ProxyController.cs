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
        private const string TargetBaseUrl = "https://galaxy.mobstudio.ru/";

        public ProxyController(IHttpClientFactory httpClientFactory, ILogger<ProxyController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // Универсальный метод для любых HTTP-запросов
        [HttpGet, HttpPost, HttpPut, HttpPatch, HttpDelete, HttpOptions]
        [Route("{*path}")]
        public async Task<IActionResult> HandleRequest(string path = "")
        {
            var client = _httpClientFactory.CreateClient();
            var targetUrl = string.IsNullOrEmpty(path)
                ? new Uri(TargetBaseUrl)
                : new Uri(new Uri(TargetBaseUrl), path);

            try
            {
                // Создаём исходящий запрос
                var method = new HttpMethod(Request.Method);
                var requestMessage = new HttpRequestMessage(method, targetUrl);
                //AddGalaxyHeaders(requestMessage);
                // Копируем все заголовки из входящего запроса
                foreach (var header in Request.Headers)
                {
                    if (!requestMessage.Headers.Contains(header.Key) &&
                        !header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
                        !header.Key.StartsWith(":"))
                    {
                        try
                        {
                            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                        }
                        catch { /* Игнорируем ошибки для невалидных заголовков */ }
                    }
                }
                // Если есть тело запроса — копируем его
                if (Request.ContentLength > 0 &&
                    (method == HttpMethod.Post || method == HttpMethod.Put || method.Method == "PATCH"))
                {
                    using var reader = new StreamReader(Request.Body);
                    var body = await reader.ReadToEndAsync();

                    var contentType = Request.ContentType ?? "application/octet-stream";

                    // Определяем тип контента
                    if (contentType.Contains("application/json"))
                    {
                        requestMessage.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    }
                    else if (contentType.Contains("application/x-www-form-urlencoded"))
                    {
                        requestMessage.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
                    }
                    else if (contentType.Contains("text/plain"))
                    {
                        requestMessage.Content = new StringContent(body, Encoding.UTF8, "text/plain");
                    }
                    else
                    {
                        // Любой другой тип (включая multipart/form-data)
                        var bytes = Encoding.UTF8.GetBytes(body);
                        requestMessage.Content = new ByteArrayContent(bytes);
                        requestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                    }
                }

                // Проксируем
                var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
                var contentTypeHeader = response.Content.Headers.ContentType?.ToString();
                var charset = response.Content.Headers.ContentType?.CharSet ?? "utf-8";

                var bytess = await response.Content.ReadAsByteArrayAsync();

                // === Обработка JS ===
                if (contentTypeHeader.Contains("javascript") || contentTypeHeader.EndsWith(".js") || contentTypeHeader.Contains("text/css"))
                {
                    var text = Encoding.UTF8.GetString(bytess);

                    // Переписываем все пути к /web/
                    text = text.Replace("https://galaxy.mobstudio.ru/", "/api/proxy/");
                    text = text.Replace("'/web/", "'/api/proxy/web/");
                    text = text.Replace("\"/web/", "\"/api/proxy/web/");

                    return Content(text, contentTypeHeader + "; charset=utf-8", Encoding.UTF8);
                }

                // Универсальная обработка контента
                if (contentTypeHeader != null && (
                    contentTypeHeader.Contains("text/html") ||
                    contentTypeHeader.Contains("application/json") ||
                    contentTypeHeader.Contains("application/xml") ||
                    contentTypeHeader.Contains("text/javascript") ||
                    contentTypeHeader.Contains("application/javascript") ||
                    contentTypeHeader.Contains("text/css")))
                {
                    var text = await response.Content.ReadAsStringAsync();

                    // Внедрение скрипта только для HTML
                    if (contentTypeHeader.Contains("text/html"))
                    {
                        var doc = new HtmlDocument();
                        doc.LoadHtml(text);

                        RewriteRelativeUrls(doc);

                        var head = doc.DocumentNode.SelectSingleNode("//head");
                        if (head != null)
                        {
                            var oldMetas = head.SelectNodes(".//meta[@charset]") ?? new HtmlNodeCollection(null);
                            foreach (var m in oldMetas) m.Remove();
                            var httpEquivMetas = head.SelectNodes(".//meta[translate(@http-equiv,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='content-type']") ?? new HtmlNodeCollection(null);
                            foreach (var m in httpEquivMetas) m.Remove();

                            var metaCharset = doc.CreateElement("meta");
                            metaCharset.SetAttributeValue("charset", "utf-8");
                            head.PrependChild(metaCharset);

                            var baseTag = doc.CreateElement("base");
                            baseTag.SetAttributeValue("href", "/api/proxy/web/");
                            head.PrependChild(baseTag);
                        }

                        var jsCode = "alert('Hello from the proxy!');";
                        var proxyScript = HtmlNode.CreateNode($"<script>{jsCode}</script>");
                        var body = doc.DocumentNode.SelectSingleNode("//body");

                        var jsInterceptor = @"(function() {
    // Перехват fetch
    const origFetch = window.fetch;
    window.fetch = function(url, opts) {
        url = rewriteUrl(url);
        return origFetch(url, opts);
    };

    // Перехват XMLHttpRequest
    const origOpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(method, url) {
        url = rewriteUrl(url);
        return origOpen.apply(this, [method, url]);
    };

    // Перехват переходов по ссылкам
    document.addEventListener('click', function(e) {
        const a = e.target.closest('a');
        if (a && a.href) {
            a.href = rewriteUrl(a.href);
        }
    }, true);

    // Перехват форм
    document.addEventListener('submit', function(e) {
        const form = e.target;
        if (form && form.action) {
            form.action = rewriteUrl(form.action);
        }
    }, true);

    function rewriteUrl(url) {
        if (!url) return url;
        if (url.startsWith('https://galaxy.mobstudio.ru/'))
            return url.replace('https://galaxy.mobstudio.ru/', '/api/proxy/');
        if (url.startsWith('/web/'))
            return '/api/proxy' + url;
        if (url.startsWith('/'))
            return '/api/proxy' + url;
        return url;
    }
})();";
                        var scriptNode = HtmlNode.CreateNode($"<script>{jsInterceptor}</script>");
                        body?.AppendChild(scriptNode);

                        if (body != null)
                        {
                            var mainScript = doc.DocumentNode.SelectSingleNode("//script[@src]");
                            if (mainScript != null)
                                mainScript.ParentNode.InsertBefore(proxyScript, mainScript);
                            else
                                body.AppendChild(proxyScript);
                        }

                        var modifiedHtml = doc.DocumentNode.OuterHtml;
                        return Content(modifiedHtml, contentTypeHeader + "; charset=utf-8", Encoding.UTF8);
                    }

                    // Для остальных текстовых типов просто возвращаем текст
                    return Content(text, contentTypeHeader + "; charset=utf-8", Encoding.UTF8);
                }
                else
                {
                    var content = await response.Content.ReadAsByteArrayAsync();
                    return new FileContentResult(content, contentTypeHeader ?? "application/octet-stream")
                    {
                        FileDownloadName = Path.GetFileName(targetUrl.LocalPath)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проксировании запроса {Method} {Url}", Request.Method, targetUrl);
                return StatusCode(500, $"Ошибка прокси: {ex.Message}");
            }
        }

        private void RewriteRelativeUrls(HtmlDocument doc)
        {
            var nodes = doc.DocumentNode.SelectNodes("//*[@src or @href or @action]");
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                foreach (var attr in new[] { "src", "href", "action" })
                {
                    var value = node.GetAttributeValue(attr, null);
                    if (string.IsNullOrEmpty(value)) continue;

                    // Игнорируем якоря, mailto, data:
                    if (value.StartsWith("#") || value.StartsWith("data:") || value.StartsWith("mailto:"))
                        continue;

                    if (value.StartsWith("https://galaxy.mobstudio.ru/"))
                    {
                        value = value.Replace("https://galaxy.mobstudio.ru/", "/api/proxy/");
                    }
                    else if (value.StartsWith("/web/"))
                    {
                        value = "/api/proxy" + value;
                    }
                    else if (value.StartsWith("/"))
                    {
                        value = "/api/proxy" + value;
                    }
                    else if (value.StartsWith("web/"))
                    {
                        value = "/api/proxy/" + value;
                    }

                    node.SetAttributeValue(attr, value);
                }
            }
        }
    }
}