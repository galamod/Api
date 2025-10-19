using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

        private void AddGalaxyHeaders(HttpRequestMessage request)
        {
            // Копируем важные заголовки из входящего запроса
            if (Request.Headers.TryGetValue("Cookie", out var cookies))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookies.ToString());
            }

            if (Request.Headers.TryGetValue("X-Requested-With", out var xRequestedWith))
            {
                request.Headers.TryAddWithoutValidation("X-Requested-With", xRequestedWith.ToString());
            }

            // Стандартные заголовки браузера
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");

            var referer = Request.Path.Value?.Contains("/services") == true
                ? "https://galaxy.mobstudio.ru/"
                : "https://galaxy.mobstudio.ru/";

            request.Headers.TryAddWithoutValidation("Origin", "https://galaxy.mobstudio.ru");
            request.Headers.TryAddWithoutValidation("Referer", referer);

            // Sec-Fetch заголовки
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");

            // Galaxy-специфичные заголовки
            foreach (var header in new[] { "x-galaxy-client-ver", "x-galaxy-kbv", "x-galaxy-lng",
                                     "x-galaxy-model", "x-galaxy-orientation", "x-galaxy-os-ver",
                                     "x-galaxy-platform", "x-galaxy-scr-dpi", "x-galaxy-scr-h",
                                     "x-galaxy-scr-w", "x-galaxy-user-agent" })
            {
                if (Request.Headers.TryGetValue(header, out var value))
                {
                    request.Headers.TryAddWithoutValidation(header, value.ToString());
                }
            }

            if (!request.Headers.Contains("x-galaxy-client-ver"))
            {
                request.Headers.TryAddWithoutValidation("x-galaxy-client-ver", "9.5");
                request.Headers.TryAddWithoutValidation("x-galaxy-kbv", "352");
                request.Headers.TryAddWithoutValidation("x-galaxy-lng", "ru");
                request.Headers.TryAddWithoutValidation("x-galaxy-model", "chrome 140.0.0.0");
                request.Headers.TryAddWithoutValidation("x-galaxy-orientation", "portrait");
                request.Headers.TryAddWithoutValidation("x-galaxy-os-ver", "1");
                request.Headers.TryAddWithoutValidation("x-galaxy-platform", "web");
                request.Headers.TryAddWithoutValidation("x-galaxy-scr-dpi", "1");
                request.Headers.TryAddWithoutValidation("x-galaxy-scr-h", "945");
                request.Headers.TryAddWithoutValidation("x-galaxy-scr-w", "700");
                request.Headers.TryAddWithoutValidation("x-galaxy-user-agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
            }
        }

        [HttpGet, HttpPost, HttpPut, HttpPatch, HttpDelete, HttpOptions]
        [Route("{*path}")]
        public async Task<IActionResult> HandleRequest(string path = "")
        {
            var client = _httpClientFactory.CreateClient("GalaxyClient");

            var queryString = Request.QueryString.HasValue ? Request.QueryString.Value : "";
            var fullPath = string.IsNullOrEmpty(path) ? "" : path + queryString;

            var targetUrl = string.IsNullOrEmpty(fullPath)
                ? new Uri(TargetBaseUrl)
                : new Uri(new Uri(TargetBaseUrl), fullPath);

            try
            {
                var method = new HttpMethod(Request.Method);
                var requestMessage = new HttpRequestMessage(method, targetUrl);
                AddGalaxyHeaders(requestMessage);

                if (Request.ContentLength > 0 &&
                    (method == HttpMethod.Post || method == HttpMethod.Put || method.Method == "PATCH"))
                {
                    var contentType = Request.ContentType ?? "application/octet-stream";

                    var ms = new MemoryStream();
                    await Request.Body.CopyToAsync(ms);
                    ms.Position = 0;

                    requestMessage.Content = new StreamContent(ms);
                    requestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                }

                _logger.LogInformation("Proxying {Method} request to {Url}", method.Method, targetUrl);

                var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);

                if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                {
                    foreach (var cookie in setCookies)
                    {
                        Response.Headers.Append("Set-Cookie", cookie);
                    }
                }

                var contentTypeHeader = response.Content.Headers.ContentType?.ToString();
                var charset = response.Content.Headers.ContentType?.CharSet ?? "utf-8";

                _logger.LogInformation("Response status: {StatusCode}, ContentType: {ContentType}",
                    response.StatusCode, contentTypeHeader);

                // Универсальная обработка контента
                if (contentTypeHeader != null && (
                    contentTypeHeader.Contains("text/html") ||
                    contentTypeHeader.Contains("application/json") ||
                    contentTypeHeader.Contains("application/xml") ||
                    contentTypeHeader.Contains("text/javascript") ||
                    contentTypeHeader.Contains("application/javascript") ||
                    contentTypeHeader.Contains("text/css") ||
                    contentTypeHeader.Contains("text/plain") ||
                    contentTypeHeader.Contains("application/manifest+json")))
                {
                    var text = await response.Content.ReadAsStringAsync();

                    if (contentTypeHeader.Contains("text/css"))
                    {
                        // ТОЛЬКО /web/assets/ делаем абсолютными
                        text = Regex.Replace(text, @"url\(\s*(['""]?)(?<!https://galaxy\.mobstudio\.ru)(/web/assets/[^)'""\s]+)\1\s*\)",
                            "url($1https://galaxy.mobstudio.ru$2$1)");
                    }
                    else if (contentTypeHeader.Contains("javascript"))
                    {
                        // ТОЛЬКО /web/assets/ делаем абсолютными
                        text = Regex.Replace(text, @"(['""])(?<!https://galaxy\.mobstudio\.ru)(/web/assets/[^'""]+)\1",
                            "$1https://galaxy.mobstudio.ru$2$1");

                        // Остальные пути (включая /services/) проксируем
                        text = Regex.Replace(text, @"https://galaxy\.mobstudio\.ru/(?!web/assets/)([^'""\s>]*)", "/api/proxy/$1");
                        text = Regex.Replace(text, @"(['""])(?<!https://galaxy\.mobstudio\.ru)(/(?:web/(?!assets/)|services/)[^'""<>]*)", "$1/api/proxy$2");

                        if (path.StartsWith("web/app.", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".js"))
                        {
                            var automationScript = GetAutomationScript();
                            text = InjectScriptRandomly(text, automationScript);
                            _logger.LogInformation("✅ Automation script injected into {Path}", path);
                        }
                    }
                    else if (contentTypeHeader.Contains("application/json") || contentTypeHeader.Contains("application/manifest+json") || path.EndsWith("manifest.json"))
                    {
                        // ТОЛЬКО /web/assets/ делаем абсолютными
                        text = Regex.Replace(text, @"""(?<!https://galaxy\.mobstudio\.ru)(/web/assets/[^""]+)""",
                            "\"https://galaxy.mobstudio.ru$1\"");

                        // Остальные пути (включая /services/) проксируем
                        text = Regex.Replace(text, @"""(?<!https://galaxy\.mobstudio\.ru|/api/proxy)(/(?:web/(?!assets/)|services/)[^""]+)""",
                            "\"/api/proxy$1\"");
                    }
                    else
                    {
                        // ИСПРАВЛЕНИЕ: УБИРАЕМ преобразование /services/public/ в абсолютные пути
                        // Теперь они будут проксироваться через /api/proxy/

                        // ТОЛЬКО /web/assets/ делаем абсолютными
                        text = Regex.Replace(text, @"(['""])/web/assets/([^'""]+)\1",
                            "$1https://galaxy.mobstudio.ru/web/assets/$2$1");

                        text = Regex.Replace(text, @"url\(\s*(['""]?)/web/assets/([^)'""\s]+)\1\s*\)",
                            "url($1https://galaxy.mobstudio.ru/web/assets/$2$1)");
                    }

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

                            var jsInterceptor = @"(function() {
    if (window.__gxProxyInjected) return;
    window.__gxProxyInjected = true;

    const proxyPrefix = '/api/proxy/';

    function rewriteUrl(url) {
        if (!url || url.startsWith('#') || url.startsWith('data:') || url.startsWith('blob:') || url.startsWith('mailto:'))
            return url;

        // Проверяем, не проксируется ли URL уже
        if (url.includes('/api/proxy/'))
            return url;

        // ИСПРАВЛЕНИЕ: /web/assets/ и PNG делаем абсолютными
        if (url.includes('/web/assets/') || url.toLowerCase().endsWith('.png')) {
            if (url.startsWith('/')) return 'https://galaxy.mobstudio.ru' + url;
            return url;
        }

        // Абсолютные URL с доменом
        if (url.startsWith('https://galaxy.mobstudio.ru/'))
            return url.replace('https://galaxy.mobstudio.ru/', proxyPrefix);
        if (url.startsWith('//galaxy.mobstudio.ru/'))
            return proxyPrefix + url.substring('//galaxy.mobstudio.ru/'.length);

        // ВСЕ остальные абсолютные пути (включая /services/, /web/)
        if (url.startsWith('/'))
            return proxyPrefix + url.substring(1);

        return url;
    }

    // Перехват fetch
    const origFetch = window.fetch;
    window.fetch = function(input, init) {
        if (typeof input === 'string') {
            input = rewriteUrl(input);
        } else if (input && input.url) {
            input = new Request(rewriteUrl(input.url), input);
        }
        return origFetch.call(this, input, init);
    };

    // Перехват XMLHttpRequest
    const origOpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(method, url, ...args) {
        return origOpen.call(this, method, rewriteUrl(url), ...args);
    };

    // Перехват кликов по ссылкам
    document.addEventListener('click', function(e) {
        const a = e.target.closest('a');
        if (a && a.href && !a.href.startsWith('javascript:')) {
            const newHref = rewriteUrl(a.href);
            if (newHref !== a.href) {
                a.href = newHref;
            }
        }
    }, true);

    // Перехват отправки форм
    document.addEventListener('submit', function(e) {
        const form = e.target;
        if (form && form.action) {
            const newAction = rewriteUrl(form.action);
            if (newAction !== form.action) {
                form.action = newAction;
            }
        }
    }, true);

    let isObserverProcessing = false;

    const observer = new MutationObserver(mutations => {
        if (isObserverProcessing) return;
        isObserverProcessing = true;

        mutations.forEach(mutation => {
            if (mutation.type === 'attributes') {
                const el = mutation.target;
                const attrName = mutation.attributeName;
                if (attrName === 'src' || attrName === 'href') {
                    const val = el.getAttribute(attrName);
                    if (val && !val.startsWith(proxyPrefix) && !val.includes('/api/proxy/') && !val.startsWith('#') && !val.startsWith('data:') && !val.startsWith('https://galaxy.mobstudio.ru')) {
                        const newVal = rewriteUrl(val);
                        if (newVal !== val) {
                            observer.disconnect();
                            el.setAttribute(attrName, newVal);
                            observer.observe(document.documentElement, {
                                attributes: true,
                                attributeFilter: ['src', 'href'],
                                subtree: true
                            });
                        }
                    }
                }
            }
        });

        setTimeout(() => { isObserverProcessing = false; }, 0);
    });

    observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['src', 'href'],
        subtree: true
    });
})();";
                            var interceptorNode = HtmlNode.CreateNode($"<script>{jsInterceptor}</script>");
                            head.PrependChild(interceptorNode);
                        }

                        var modifiedHtml = doc.DocumentNode.OuterHtml;

                        _logger.LogInformation("✅ HTML modified and returned. Size: {Size} bytes", modifiedHtml.Length);

                        return Content(modifiedHtml, contentTypeHeader + "; charset=utf-8", Encoding.UTF8);
                    }

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
            var nodes = doc.DocumentNode.SelectNodes("//*[@src or @href or @action or @data]");
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                foreach (var attr in new[] { "src", "href", "action", "data" })
                {
                    var value = node.GetAttributeValue(attr, null);
                    if (string.IsNullOrEmpty(value)) continue;

                    if (value.StartsWith("#") || value.StartsWith("data:") || value.StartsWith("blob:") ||
                        value.StartsWith("mailto:") || value.StartsWith("javascript:"))
                        continue;

                    // Пропускаем уже проксированные URL
                    if (value.Contains("/api/proxy/"))
                        continue;

                    // ИСПРАВЛЕНИЕ: ТОЛЬКО /web/assets/ и PNG делаем абсолютными
                    if (value.Contains("/web/assets/") || value.ToLower().EndsWith(".png"))
                    {
                        if (value.StartsWith("/"))
                        {
                            node.SetAttributeValue(attr, "https://galaxy.mobstudio.ru" + value);
                        }
                        continue;
                    }

                    // ВСЕ остальное (включая /services/) проксируем
                    if (value.StartsWith("https://galaxy.mobstudio.ru/"))
                    {
                        value = value.Replace("https://galaxy.mobstudio.ru/", "/api/proxy/");
                    }
                    else if (value.StartsWith("//galaxy.mobstudio.ru/"))
                    {
                        value = "/api/proxy/" + value.Substring("//galaxy.mobstudio.ru/".Length);
                    }
                    else if (value.StartsWith("/"))
                    {
                        value = "/api/proxy" + value;
                    }
                    else if (value.StartsWith("web/") || value.StartsWith("services/"))
                    {
                        value = "/api/proxy/" + value;
                    }

                    node.SetAttributeValue(attr, value);
                }
            }

            var nodesWithStyle = doc.DocumentNode.SelectNodes("//*[@style]");
            if (nodesWithStyle != null)
            {
                foreach (var node in nodesWithStyle)
                {
                    var style = node.GetAttributeValue("style", "");
                    if (style.Contains("url("))
                    {
                        style = Regex.Replace(style,
                            @"url\(\s*(['""]?)(/[^)'""\s]+)\1\s*\)",
                            m =>
                            {
                                var path = m.Groups[2].Value;

                                if (path.StartsWith("/api/proxy"))
                                    return m.Value;

                                // ТОЛЬКО /web/assets/ и PNG делаем абсолютными
                                if (path.Contains("/web/assets/") || path.ToLower().EndsWith(".png"))
                                    return $"url({m.Groups[1].Value}https://galaxy.mobstudio.ru{path}{m.Groups[1].Value})";

                                // Остальное проксируем
                                return $"url({m.Groups[1].Value}/api/proxy{path}{m.Groups[1].Value})";
                            });
                        node.SetAttributeValue("style", style);
                    }
                }
            }
        }

        private string GetAutomationScript()
        {
            var jsCode = "alert('Hello, world!');";

            var bytes = new UTF8Encoding(false).GetBytes(jsCode);
            var base64 = Convert.ToBase64String(bytes);

            var chunkSize = 100;
            var chunks = new List<string>();
            for (int i = 0; i < base64.Length; i += chunkSize)
            {
                chunks.Add(base64.Substring(i, Math.Min(chunkSize, base64.Length - i)));
            }

            var chunksJson = JsonSerializer.Serialize(chunks);
            var wrapped = $@"
(function(p, c) {{
    try {{
        var d = function(e) {{ return decodeURIComponent(escape(atob(e))); }};
        var s = p.join(c);
        new Function(d(s))();
    }} catch (e) {{
        console.error('gx-loader: ' + e.message);
    }}
}})({chunksJson}, '');
";
            return wrapped;
        }

        private string InjectScriptRandomly(string originalJs, string scriptToInject)
        {
            var injectionPoints = new List<int>();
            var regex = new Regex(@";\s*[\r\n]|\}\s*[\r\n]+\s*(var|let|const|function|if|\(|\{|\[)");

            var safeSearchArea = Regex.Replace(originalJs, @"("".*?""|'.*?'|`.*?`|//.*|/\*.*?\*/)", m => new string(' ', m.Length));

            var matches = regex.Matches(safeSearchArea);
            foreach (Match match in matches)
            {
                injectionPoints.Add(match.Index + 1);
            }

            if (injectionPoints.Count > 0)
            {
                var random = new Random();
                var index = injectionPoints[random.Next(injectionPoints.Count)];
                return originalJs.Insert(index, "\n" + scriptToInject + "\n");
            }
            else
            {
                return originalJs + scriptToInject;
            }
        }
    }
}