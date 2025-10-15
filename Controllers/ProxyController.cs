using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
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
        // Однократная загрузка обфусцированного скрипта и вычисление ETag/версии
        private static readonly Lazy<(byte[] Bytes, string ETag, string Version)> ObfScript = new(() =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "proxy", "script.obf.js");
            if (!System.IO.File.Exists(path))
            {
                return (Array.Empty<byte>(), "\"dev\"", "dev");
            }

            var bytes = System.IO.File.ReadAllBytes(path);
            using var sha256 = SHA256.Create();
            var hash = Convert.ToHexString(sha256.ComputeHash(bytes)).ToLowerInvariant();
            var etag = $"W/\"{hash}\"";
            var version = hash[..16]; // короткий токен для ?v=
            return (bytes, etag, version);
        }, isThreadSafe: true);
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

            // Копируем X-Requested-With (часто требуется для AJAX)
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

            // ВАЖНО: Для /services/ используем правильный Referer
            var referer = Request.Path.Value?.Contains("/services") == true
                ? "https://galaxy.mobstudio.ru/"
                : "https://galaxy.mobstudio.ru/";

            request.Headers.TryAddWithoutValidation("Origin", "https://galaxy.mobstudio.ru");
            request.Headers.TryAddWithoutValidation("Referer", referer);

            // Sec-Fetch заголовки (КРИТИЧЕСКИ ВАЖНО для /services/)
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

            // User-Agent
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");

            // Galaxy-специфичные заголовки (КОПИРУЕМ ИХ ИЗ ВХОДЯЩЕГО ЗАПРОСА!)
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

            // Если заголовки не были скопированы - используем значения по умолчанию
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

        // Универсальный метод для любых HTTP-запросов
        [HttpGet, HttpPost, HttpPut, HttpPatch, HttpDelete, HttpOptions]
        [Route("{*path}")]
        public async Task<IActionResult> HandleRequest(string path = "")
        {
            var client = _httpClientFactory.CreateClient("GalaxyClient");

            // ВАЖНО: Добавляем query string из оригинального запроса
            var queryString = Request.QueryString.HasValue ? Request.QueryString.Value : "";
            var fullPath = string.IsNullOrEmpty(path) ? "" : path + queryString;

            var targetUrl = string.IsNullOrEmpty(fullPath)
                ? new Uri(TargetBaseUrl)
                : new Uri(new Uri(TargetBaseUrl), fullPath);

            try
            {
                // Создаём исходящий запрос
                var method = new HttpMethod(Request.Method);
                var requestMessage = new HttpRequestMessage(method, targetUrl);
                AddGalaxyHeaders(requestMessage);

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

                _logger.LogInformation("Proxying {Method} request to {Url}", method.Method, targetUrl);
                _logger.LogInformation("Request headers: {Headers}", 
                    string.Join(", ", requestMessage.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}")));

                // Проксируем
                var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
                var contentTypeHeader = response.Content.Headers.ContentType?.ToString();
                var charset = response.Content.Headers.ContentType?.CharSet ?? "utf-8";

                _logger.LogInformation("Response status: {StatusCode}, ContentType: {ContentType}", 
                    response.StatusCode, contentTypeHeader);

                // ВАЖНО: Для /services/public/ И manifest.json просто возвращаем как есть (БЕЗ МОДИФИКАЦИИ)
                if (path.StartsWith("services/public/", StringComparison.OrdinalIgnoreCase))
                {
                    var content = await response.Content.ReadAsByteArrayAsync();

                    // Добавляем CORS-заголовки для манифеста
                    Response.Headers.Append("Access-Control-Allow-Origin", "*");
                    Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

                    return new FileContentResult(content, contentTypeHeader ?? "application/octet-stream");
                }

                // Универсальная обработка контента
                if (contentTypeHeader != null && (
                    contentTypeHeader.Contains("text/html") ||
                    contentTypeHeader.Contains("application/json") ||
                    contentTypeHeader.Contains("application/xml") ||
                    contentTypeHeader.Contains("text/javascript") ||
                    contentTypeHeader.Contains("application/javascript") ||
                    contentTypeHeader.Contains("text/css") ||
                    contentTypeHeader.Contains("text/plain") ||
                    contentTypeHeader.Contains("application/manifest+json"))) // Добавляем manifest
                {
                    var text = await response.Content.ReadAsStringAsync();

                    // СПЕЦИАЛЬНАЯ ОБРАБОТКА ДЛЯ CSS - переписываем /web/assets/ на абсолютные пути
                    if (contentTypeHeader.Contains("text/css"))
                    {
                        // В CSS-файлах заменяем url(/web/assets/...) на url(https://galaxy.mobstudio.ru/web/assets/...)
                        // ВАЖНО: Проверяем, что перед /web/assets/ НЕТ уже домена
                        text = Regex.Replace(text, @"url\(\s*(['""]?)(?<!https://galaxy\.mobstudio\.ru)(/web/assets/[^)'""\s]+)\1\s*\)",
                            "url($1https://galaxy.mobstudio.ru$2$1)");
                    }
                    // СПЕЦИАЛЬНАЯ ОБРАБОТКА ДЛЯ JS - переписываем строки с /web/assets/ на абсолютные пути
                    else if (contentTypeHeader.Contains("javascript"))
                    {
                        // В JS-файлах заменяем строковые литералы, но только если перед /web/assets/ нет домена
                        text = Regex.Replace(text, @"(['""])(?<!https://galaxy\.mobstudio\.ru)(/web/assets/[^'""]+)\1",
                            "$1https://galaxy.mobstudio.ru$2$1");

                        // Для остальных путей (НЕ /web/assets/) применяем обычное проксирование
                        text = Regex.Replace(text, @"https://galaxy\.mobstudio\.ru/(?!web/assets/)([^'""\s>]*)", "/api/proxy/$1");
                        text = Regex.Replace(text, @"(['""])(?<!https://galaxy\.mobstudio\.ru)(/web/(?!assets/)[^'""<>]*)", "$1/api/proxy$2");
                    }
                    // СПЕЦИАЛЬНАЯ ОБРАБОТКА ДЛЯ MANIFEST.JSON - переписываем /web/assets/ на абсолютные пути
                    else if (contentTypeHeader.Contains("application/json") || contentTypeHeader.Contains("application/manifest+json") || path.EndsWith("manifest.json"))
                    {
                        // В JSON/Manifest заменяем строки "/web/assets/...", но только если перед ними нет домена
                        text = Regex.Replace(text, @"""(?<!https://galaxy\.mobstudio\.ru)(/web/assets/[^""]+)""",
                            "\"https://galaxy.mobstudio.ru$1\"");

                        // Для остальных /web/ путей (НЕ /web/assets/) применяем обычное проксирование
                        text = Regex.Replace(text, @"""(?<!https://galaxy\.mobstudio\.ru|/api/proxy)(/web/(?!assets/)[^""]+)""",
                            "\"/api/proxy$1\"");
                    }
                    else
                    {
                        // Для HTML - обработка спецпутей в строках

                        // 1. /services/public/ - делаем абсолютными
                        text = Regex.Replace(text, @"(['""])/services/public/([^'""]+)\1",
                            "$1https://galaxy.mobstudio.ru/services/public/$2$1");

                        // 2. /web/assets/ - делаем абсолютными
                        text = Regex.Replace(text, @"(['""])/web/assets/([^'""]+)\1",
                            "$1https://galaxy.mobstudio.ru/web/assets/$2$1");

                        text = Regex.Replace(text, @"url\(\s*(['""]?)/web/assets/([^)'""\s]+)\1\s*\)",
                            "url($1https://galaxy.mobstudio.ru/web/assets/$2$1)");
                    }

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

                            // 1) Перехватчик: вставляем ПЕРВЫМ в <head>, чтобы он сработал раньше любых других скриптов
                            var jsInterceptor = @"(function() {
    if (window.__gxProxyInjected) return;
    window.__gxProxyInjected = true;

    const proxyPrefix = '/api/proxy/';

    function rewriteUrl(url) {
        if (!url || url.startsWith('#') || url.startsWith('data:') || url.startsWith('blob:') || url.startsWith('mailto:'))
            return url;

        // НЕ переписываем PNG изображения - оставляем оригинальные пути
        if (url.toLowerCase().endsWith('.png')) {
            if (url.startsWith('/web/')) return 'https://galaxy.mobstudio.ru' + url;
            if (url.startsWith('/')) return 'https://galaxy.mobstudio.ru' + url;
            return url;
        }

        // Абсолютные URL с доменом
        if (url.startsWith('https://galaxy.mobstudio.ru/'))
            return url.replace('https://galaxy.mobstudio.ru/', proxyPrefix);
        if (url.startsWith('//galaxy.mobstudio.ru/'))
            return proxyPrefix + url.substring('//galaxy.mobstudio.ru/'.length);

        // Явно обрабатываем /web/
        if (url.startsWith('/web/'))
            return proxyPrefix + url.substring(1);

        // Остальные абсолютные пути
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
            a.href = rewriteUrl(a.href);
        }
    }, true);

    // Перехват отправки форм
    document.addEventListener('submit', function(e) {
        const form = e.target;
        if (form && form.action) {
            form.action = rewriteUrl(form.action);
        }
    }, true);

    // Защита от рекурсии наблюдателя
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
                    if (val && !val.startsWith(proxyPrefix) && !val.startsWith('#') && !val.startsWith('data:') && !val.startsWith('https://galaxy.mobstudio.ru')) {
                        const newVal = rewriteUrl(val);
                        if (newVal !== val) {
                            el.setAttribute(attrName, newVal);
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

                            // 2) Прелоад и defer-скрипт с версией по хешу (устойчиво к таймингу)
                            var versionToken = ObfScript.Value.Version;
                            var preload = doc.CreateElement("link");
                            preload.SetAttributeValue("rel", "preload");
                            preload.SetAttributeValue("as", "script");
                            preload.SetAttributeValue("href", $"/api/proxy/script.js?v={versionToken}");
                            head.PrependChild(preload);

                            var external = doc.CreateElement("script");
                            external.SetAttributeValue("src", $"/api/proxy/script.js?v={versionToken}");
                            external.SetAttributeValue("defer", null);
                            external.SetAttributeValue("data-proxy", "main");
                            head.PrependChild(external);
                        }

                        var modifiedHtml = doc.DocumentNode.OuterHtml;

                        _logger.LogInformation("✅ HTML modified and returned. Size: {Size} bytes", modifiedHtml.Length);

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

        // Отдаём уже обфусцированный JS без Base64/«eval»
        [HttpGet]
        [Route("script.js")]
        public IActionResult GetEncodedScript()
        {
            var (bytes, etag, version) = ObfScript.Value;

            if (bytes.Length == 0)
            {
                // Файл ещё не сгенерирован — отдаём заглушку без кэширования
                Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                Response.Headers.Append("Pragma", "no-cache");
                Response.Headers.Append("Expires", "0");
                _logger.LogWarning("⚠️ Obfuscated script not found: wwwroot/proxy/script.obf.js");
                return Content("// script not available", "application/javascript; charset=utf-8");
            }

            // 304 по ETag
            if (Request.Headers.TryGetValue("If-None-Match", out var inm) &&
                inm.ToString().Split(',').Any(t => string.Equals(t.Trim(), etag, StringComparison.Ordinal)))
            {
                Response.Headers["ETag"] = etag;
                Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
                return StatusCode(304);
            }

            Response.Headers["ETag"] = etag;
            Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
            Response.Headers["Content-Type"] = "application/javascript; charset=utf-8";

            _logger.LogInformation("✅ Obfuscated script returned. Size: {Size} bytes, ETag: {ETag}", bytes.Length, etag);

            return File(bytes, "application/javascript; charset=utf-8");
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

                    // Игнорируем специальные протоколы
                    if (value.StartsWith("#") || value.StartsWith("data:") || value.StartsWith("blob:") ||
                        value.StartsWith("mailto:") || value.StartsWith("javascript:"))
                        continue;

                    // НЕ переписываем PNG изображения - оставляем оригинальные пути
                    if (value.ToLower().EndsWith(".png"))
                    {
                        // Если это относительный путь к PNG, делаем его абсолютным к оригинальному серверу
                        if (value.StartsWith("/web/"))
                        {
                            node.SetAttributeValue(attr, "https://galaxy.mobstudio.ru" + value);
                        }
                        else if (value.StartsWith("/"))
                        {
                            node.SetAttributeValue(attr, "https://galaxy.mobstudio.ru" + value);
                        }
                        continue;
                    }

                    // Абсолютные URL с доменом
                    if (value.StartsWith("https://galaxy.mobstudio.ru/"))
                    {
                        value = value.Replace("https://galaxy.mobstudio.ru/", "/api/proxy/");
                    }
                    else if (value.StartsWith("//galaxy.mobstudio.ru/"))
                    {
                        value = "/api/proxy/" + value.Substring("//galaxy.mobstudio.ru/".Length);
                    }
                    // Пути, начинающиеся с /web/
                    else if (value.StartsWith("/web/"))
                    {
                        value = "/api/proxy" + value;
                    }
                    // Все остальные абсолютные пути
                    else if (value.StartsWith("/"))
                    {
                        value = "/api/proxy" + value;
                    }
                    // Относительные пути
                    else if (value.StartsWith("web/"))
                    {
                        value = "/api/proxy/" + value;
                    }

                    node.SetAttributeValue(attr, value);
                }
            }

            // Обрабатываем inline styles (БЕЗ /services/public/)
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
                            m => {
                                var path = m.Groups[2].Value;

                                if (path.StartsWith("/api/proxy"))
                                    return m.Value;

                                // /web/assets/ - напрямую
                                if (path.Contains("/web/assets/"))
                                    return $"url({m.Groups[1].Value}https://galaxy.mobstudio.ru{path}{m.Groups[1].Value})";

                                // PNG - напрямую
                                if (path.ToLower().EndsWith(".png"))
                                    return $"url({m.Groups[1].Value}https://galaxy.mobstudio.ru{path}{m.Groups[1].Value})";

                                // Остальное проксируем
                                return $"url({m.Groups[1].Value}/api/proxy{path}{m.Groups[1].Value})";
                            });
                        node.SetAttributeValue("style", style);
                    }
                }
            }
        }
    }
}