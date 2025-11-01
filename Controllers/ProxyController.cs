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

            // 🔥 КРИТИЧНО: Определяем тип запроса ДО выполнения
            var acceptHeader = Request.Headers["Accept"].ToString();
            var xRequestedWith = Request.Headers["X-Requested-With"].ToString();
            var secFetchMode = Request.Headers["Sec-Fetch-Mode"].ToString();
            var secFetchDest = Request.Headers["Sec-Fetch-Dest"].ToString();
            
            // AJAX запрос только если ЯВНО указан XMLHttpRequest или Accept содержит ТОЛЬКО application/json
            var isAjaxRequest = xRequestedWith == "XMLHttpRequest" || 
                               (acceptHeader.Contains("application/json") && !acceptHeader.Contains("text/html"));
            
            // Fetch API запросы (но НЕ навигационные)
            var isFetchRequest = secFetchMode == "cors" && secFetchDest != "document";
            
            // Навигационные запросы имеют приоритет
            var isNavigationRequest = secFetchMode == "navigate" || 
                                     secFetchDest == "document" ||
                                     (string.IsNullOrEmpty(path) || path == "web/" || path == "web/index.html") ||
                                     acceptHeader.Contains("text/html");
            
            _logger.LogInformation("🔍 Request analysis: Path={Path}, Accept={Accept}, XReqWith={XReq}, SecMode={SecMode}, SecDest={SecDest}", 
                path, acceptHeader, xRequestedWith, secFetchMode, secFetchDest);
            _logger.LogInformation("🔍 Flags: isAjax={IsAjax}, isFetch={IsFetch}, isNavigation={IsNav}", 
                isAjaxRequest, isFetchRequest, isNavigationRequest);

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
                    // 🔥 ВАЖНО: Используем EnableBuffering для возможности повторного чтения
                    Request.EnableBuffering();
                    
                    using var reader = new StreamReader(Request.Body, leaveOpen: true);
                    var body = await reader.ReadToEndAsync();
                    Request.Body.Position = 0; // Сбрасываем позицию для возможного повторного чтения

                    var contentType = Request.ContentType ?? "application/x-www-form-urlencoded";

                    _logger.LogInformation("📦 Request body: {Body}", body.Length > 500 ? body.Substring(0, 500) + "..." : body);

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
                    else if (contentType.Contains("multipart/form-data"))
                    {
                        // 🔥 ИСПРАВЛЕНО: Для multipart/form-data используем ByteArrayContent, а не StreamContent
                        // т.к. стрим уже прочитан
                        Request.Body.Position = 0;
                        using var memoryStream = new MemoryStream();
                        await Request.Body.CopyToAsync(memoryStream);
                        var bytes = memoryStream.ToArray();
                        
                        requestMessage.Content = new ByteArrayContent(bytes);
                        requestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                    }
                    else
                    {
                        // Любой другой тип
                        requestMessage.Content = new StringContent(body, Encoding.UTF8, contentType);
                    }
                }
                // 🔥 НОВОЕ: Обработка запросов БЕЗ Content-Length (chunked encoding)
                else if ((method == HttpMethod.Post || method == HttpMethod.Put || method.Method == "PATCH") 
                         && Request.Body.CanRead)
                {
                    try
                    {
                        Request.EnableBuffering();
                        using var reader = new StreamReader(Request.Body, leaveOpen: true);
                        var body = await reader.ReadToEndAsync();
                        Request.Body.Position = 0;
                        
                        if (!string.IsNullOrEmpty(body))
                        {
                            var contentType = Request.ContentType ?? "application/x-www-form-urlencoded";
                            _logger.LogInformation("📦 Request body (no Content-Length): {Body}", body.Length > 500 ? body.Substring(0, 500) + "..." : body);
                            requestMessage.Content = new StringContent(body, Encoding.UTF8, contentType);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Failed to read request body");
                    }
                }

                _logger.LogInformation("Proxying {Method} request to {Url}", method.Method, targetUrl);
                _logger.LogInformation("Request headers: {Headers}",
                    string.Join(", ", requestMessage.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}")));

                // Проксируем
                var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
                var contentTypeHeader = response.Content.Headers.ContentType?.ToString();

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

                    // СПЕЦИАЛЬНАЯ ОБРАБОТКА ДЛЯ CSS
                    if (contentTypeHeader.Contains("text/css"))
                    {
                        text = Regex.Replace(text, @"url\(\s*(['""]?)(?<!https://galaxy\.mobstudio\.ru)(/web/assets/[^)'""\s]+)\1\s*\)",
                            "url($1https://galaxy.mobstudio.ru$2$1)");
                    }
                    // СПЕЦИАЛЬНАЯ ОБРАБОТКА ДЛЯ JS
                    else if (contentTypeHeader.Contains("javascript"))
                    {
                        text = Regex.Replace(text, @"(['""])(?<!https://galaxy\.mobstudio\.ru)(/web/assets/[^'""]+)\1",
                            "$1https://galaxy.mobstudio.ru$2$1");

                        text = Regex.Replace(text, @"https://galaxy\.mobstudio\.ru/(?!web/assets/)([^'""\s>]*)", "/api/proxy/$1");
                        text = Regex.Replace(text, @"(['""])(?<!https://galaxy\.mobstudio\.ru)(/web/(?!assets/)[^'""<>]*)", "$1/api/proxy$2");

                        // Внедряем скрипт автоматизации в основной JS-файл приложения
                        if (path.StartsWith("web/app.", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".js"))
                        {
                            var automationScript = GetAutomationScript();
                            text = InjectScriptRandomly(text, automationScript);
                            _logger.LogInformation("✅ Automation script injected into {Path}", path);
                        }
                    }
                    // СПЕЦИАЛЬНАЯ ОБРАБОТКА ДЛЯ JSON
                    else if (contentTypeHeader.Contains("application/json") || contentTypeHeader.Contains("application/manifest+json") || path.EndsWith("manifest.json"))
                    {
                        text = Regex.Replace(text, @"""(?<!https://galaxy\.mobstudio\.ru)(/web/assets/[^""]+)""",
                            "\"https://galaxy.mobstudio.ru$1\"");

                        text = Regex.Replace(text, @"""(?<!https://galaxy\.mobstudio\.ru|/api/proxy)(/web/(?!assets/)[^""]+)""",
                            "\"/api/proxy$1\"");
                    }
                    // ОБРАБОТКА HTML
                    else if (contentTypeHeader.Contains("text/html"))
                    {
                        // 🔥 КРИТИЧЕСКО: Навигационные запросы имеют ПРИОРИТЕТ
                        // Если это навигация - ВСЕГДА возвращаем полную страницу
                        if (isNavigationRequest)
                        {
                            _logger.LogInformation("🌐 Navigation request detected - processing full HTML page");

                            // Для HTML - обработка спецпутей в строках
                            text = Regex.Replace(text, @"(['""])/services/public/([^'""]+)\1",
                                "$1https://galaxy.mobstudio.ru/services/public/$2$1");

                            text = Regex.Replace(text, @"(['""])/web/assets/([^'""]+)\1",
                                "$1https://galaxy.mobstudio.ru/web/assets/$2$1");

                            text = Regex.Replace(text, @"url\(\s*(['""]?)/web/assets/([^)'""\s]+)\1\s*\)",
                                "url($1https://galaxy.mobstudio.ru/web/assets/$2$1)");

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

                                // Внедряем скрипты ВСЕГДА для навигационных HTML страниц
                                InjectScriptsToHead(head, doc);
                            }

                            // 🔥 НОВОЕ: Убеждаемся, что в body есть контейнер .browser для services
                            if (path.StartsWith("services/") || path.Contains("/services/"))
                            {
                                var body = doc.DocumentNode.SelectSingleNode("//body");
                                if (body != null)
                                {
                                    _logger.LogInformation("⚠️ Services page detected - leaving body empty for dynamic content");
                                    
                                    // Удаляем весь контент кроме скриптов
                                    var bodyChildren = body.ChildNodes.ToList();
                                    foreach (var child in bodyChildren)
                                    {
                                        // Оставляем только <script> теги
                                        if (child.Name != "script")
                                        {
                                            child.Remove();
                                        }
                                    }
                                }
                            }

                            var modifiedHtml = doc.DocumentNode.OuterHtml;

                            _logger.LogInformation("✅ Full HTML page returned. Size: {Size} bytes", modifiedHtml.Length);

                            return Content(modifiedHtml, contentTypeHeader + "; charset=utf-8", Encoding.UTF8);
                        }
                        
                        // Только ЕСЛИ это точно AJAX/Fetch и НЕ навигация - возвращаем фрагмент
                        if ((isAjaxRequest || isFetchRequest) && !isNavigationRequest)
                        {
                            _logger.LogInformation("⚠️ AJAX/Fetch request returned HTML - extracting content only");
                            
                            var ajaxDoc = new HtmlDocument();
                            ajaxDoc.LoadHtml(text);
                            
                            // Перезаписываем URL в контенте
                            RewriteRelativeUrls(ajaxDoc);
                            
                            // Извлекаем только содержимое body (или весь документ, если body нет)
                            var bodyNode = ajaxDoc.DocumentNode.SelectSingleNode("//body");
                            var contentToReturn = bodyNode?.InnerHtml ?? ajaxDoc.DocumentNode.InnerHtml;

                            // Применяем дополнительные regex-замены для inline styles и атрибутов
                            contentToReturn = Regex.Replace(contentToReturn, @"(['""])/services/public/([^'""]+)\1",
                                "$1https://galaxy.mobstudio.ru/services/public/$2$1");

                            contentToReturn = Regex.Replace(contentToReturn, @"(['""])/web/assets/([^'""]+)\1",
                                "$1https://galaxy.mobstudio.ru/web/assets/$2$1");

                            contentToReturn = Regex.Replace(contentToReturn, @"url\(\s*(['""]?)/web/assets/([^)'""\s]+)\1\s*\)",
                                "url($1https://galaxy.mobstudio.ru/web/assets/$2$1)");

                            _logger.LogInformation("✅ Content fragment returned. Size: {Size} bytes", contentToReturn.Length);

                            // Возвращаем только контент, без обёрток
                            return Content(contentToReturn, "text/html; charset=utf-8", Encoding.UTF8);
                        }

                        // FALLBACK: Если не определили тип - возвращаем полную страницу (безопасно)
                        _logger.LogInformation("⚠️ Undefined request type - returning full HTML page as fallback");

                        text = Regex.Replace(text, @"(['""])/services/public/([^'""]+)\1",
                            "$1https://galaxy.mobstudio.ru/services/public/$2$1");

                        text = Regex.Replace(text, @"(['""])/web/assets/([^'""]+)\1",
                            "$1https://galaxy.mobstudio.ru/web/assets/$2$1");

                        text = Regex.Replace(text, @"url\(\s*(['""]?)/web/assets/([^)'""\s]+)\1\s*\)",
                            "url($1https://galaxy.mobstudio.ru/web/assets/$2$1)");

                        var fallbackDoc = new HtmlDocument();
                        fallbackDoc.LoadHtml(text);
                        RewriteRelativeUrls(fallbackDoc);

                        var fallbackHead = fallbackDoc.DocumentNode.SelectSingleNode("//head");
                        if (fallbackHead != null)
                        {
                            var oldMetas = fallbackHead.SelectNodes(".//meta[@charset]") ?? new HtmlNodeCollection(null);
                            foreach (var m in oldMetas) m.Remove();
                            var httpEquivMetas = fallbackHead.SelectNodes(".//meta[translate(@http-equiv,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='content-type']") ?? new HtmlNodeCollection(null);
                            foreach (var m in httpEquivMetas) m.Remove();

                            var metaCharset = fallbackDoc.CreateElement("meta");
                            metaCharset.SetAttributeValue("charset", "utf-8");
                            fallbackHead.PrependChild(metaCharset);

                            var baseTag = fallbackDoc.CreateElement("base");
                            baseTag.SetAttributeValue("href", "/api/proxy/web/");
                            fallbackHead.PrependChild(baseTag);

                            InjectScriptsToHead(fallbackHead, fallbackDoc);
                        }

                        return Content(fallbackDoc.DocumentNode.OuterHtml, contentTypeHeader + "; charset=utf-8", Encoding.UTF8);
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
                
                // Возвращаем более детальную информацию об ошибке
                var errorDetails = new
                {
                    error = "Proxy error",
                    message = ex.Message,
                    innerException = ex.InnerException?.Message,
                    stackTrace = ex.StackTrace?.Split('\n').Take(5).ToArray(),
                    requestMethod = Request.Method,
                    requestPath = path,
                    targetUrl = targetUrl.ToString()
                };
                
                return StatusCode(500, errorDetails);
            }
        }

        // 🔥 НОВЫЙ МЕТОД: Определяет, является ли запрос навигацией
        private bool IsNavigationRequest(string path, string contentType)
        {
            // Основные признаки навигации:
            // 1. Путь к HTML странице (пустой, web/, services/)
            // 2. Content-Type содержит text/html
            // 3. Запрос НЕ к API/assets/data endpoints
            
            if (string.IsNullOrEmpty(path) || path == "web/" || path.StartsWith("web/index"))
                return true;

            if (path.StartsWith("services/") && !path.Contains("/api/") && !path.Contains("/data/"))
                return true;

            if (contentType?.Contains("text/html") == true)
                return true;

            return false;
        }

        // 🔥 НОВЫЙ МЕТОД: Инъекция всех необходимых скриптов
        private void InjectScriptsToHead(HtmlNode head, HtmlDocument doc)
        {
            var errorSuppressor = @"<script>
// 1. ЗАГЛУШКИ для методов, которые приложение ожидает
(function() {
    'use strict';
    
    // 🔥 КРИТИЧЕСКИ: Инициализируем nativeApi ПЕРВЫМ делом
    // browser-298.js ожидает, что эта переменная уже существует
    window.nativeApi = null; // Будет установлено через setNativeApi
    
    // Создаём полную заглушку getBrowserApi с ВСЕМИ необходимыми методами
    if (!window.getBrowserApi) {
        window.getBrowserApi = function() {
            console.log('✅ getBrowserApi stub called');
            return {
                version: '1.0.0',
                platform: 'web',
                isWebView: false,
                
                // КРИТИЧЕСКИ: Метод setup, который вызывается приложением
                setup: function(config) {
                    console.log('✅ browserApi.setup called', config);
                    
                    // Если передан HTML-контент - отображаем его
                    if (config && config.html) {
                        try {
                            // ВАЖНО: Сохраняем конфигурацию в глобальную область ПЕРЕД вставкой HTML
                            window.__browserApiConfig = config;
                            
                            // Делаем переменные доступными глобально для browser-298.js
                            window.html = config.html;
                            window.extraInformation = config.extraInformation;
                            window.cssUrl = config.cssUrl;
                            window.jsUrl = config.jsUrl;
                            window.serviceJssUrls = config.serviceJssUrls || [];
                            
                            // 🔥 КРИТИЧЕСКИ: НЕ заменяем весь body, а создаём контейнер с классом .browser
                            // Это то, что ожидает browser-298.js!
                            let browserContainer = document.querySelector('.browser');
                            if (!browserContainer) {
                                browserContainer = document.createElement('div');
                                browserContainer.className = 'browser';
                                // 🔥 ВАЖНО: Добавляем базовые стили для видимости контейнера
                                browserContainer.style.cssText = 'width: 100%; min-height: 100vh; position: relative;';
                                document.body.appendChild(browserContainer);
                                console.log('✅ Created .browser container');
                            } else {
                                console.log('⚠️ Browser container already exists');
                            }
                            
                            // 🔥 ВАЖНО: Проверяем, не вставлен ли уже контент
                            const currentContent = browserContainer.innerHTML.trim();
                            const newContent = config.html.trim();
                            
                            if (currentContent === '' || currentContent !== newContent) {
                                // Вставляем HTML в контейнер .browser (НЕ в body!)
                                browserContainer.innerHTML = config.html;
                                console.log('✅ HTML inserted into .browser container');
                            } else {
                                console.log('⚠️ Content already present, skipping insertion');
                            }
                            
                            // Загружаем CSS, если указан
                            if (config.cssUrl) {
                                // Проверяем, не загружен ли уже этот CSS
                                const existingLink = document.querySelector(`link[href='${config.cssUrl}']`);
                                if (!existingLink) {
                                    const link = document.createElement('link');
                                    link.rel = 'stylesheet';
                                    link.href = config.cssUrl;
                                    document.head.appendChild(link);
                                    console.log('✅ CSS loaded:', config.cssUrl);
                                }
                            }
                            
                            // Загружаем дополнительные JS-файлы, если указаны
                            if (config.serviceJssUrls && config.serviceJssUrls.length > 0) {
                                config.serviceJssUrls.forEach(url => {
                                    const existingScript = document.querySelector(`script[src='${url}']`);
                                    if (!existingScript) {
                                        const script = document.createElement('script');
                                        script.src = url;
                                        document.head.appendChild(script);
                                        console.log('✅ Service JS loaded:', url);
                                    }
                                });
                            }
                            
                            // 🔥 ВАЖНО: Загружаем основной JavaScript ПОСЛЕ вставки HTML
                            // Иначе скрипт не найдёт контейнер .browser
                            if (config.jsUrl) {
                                const existingScript = document.querySelector(`script[src='${config.jsUrl}']`);
                                if (!existingScript) {
                                    const script = document.createElement('script');
                                    script.src = config.jsUrl;
                                    script.onload = function() {
                                        console.log('✅ Main JS loaded and executed:', config.jsUrl);
                                        
                                        // 🔥 НОВОЕ: После загрузки скрипта пытаемся инициализировать его
                                        // если есть функция инициализации
                                        if (window.initBrowserService && typeof window.initBrowserService === 'function') {
                                            try {
                                                window.initBrowserService();
                                                console.log('✅ Browser service initialized');
                                            } catch (err) {
                                                console.warn('⚠️ Failed to init browser service:', err);
                                            }
                                        }
                                    };
                                    script.onerror = function() {
                                        console.error('❌ Failed to load JS:', config.jsUrl);
                                    };
                                    // Даём браузеру время отрендерить DOM перед загрузкой скрипта
                                    setTimeout(() => {
                                        document.head.appendChild(script);
                                    }, 50);
                                } else {
                                    console.log('⚠️ JS already loaded:', config.jsUrl);
                                }
                            }
                            
                            console.log('✅ Content loaded from setup()');
                        } catch (err) {
                            console.error('❌ Error loading content:', err);
                        }
                    }
                    
                    return Promise.resolve();
                },
                
                // Дополнительные методы
                openUrl: function(url) { 
                    console.log('✅ browserApi.openUrl:', url);
                    window.open(url, '_blank'); 
                },
                close: function() { 
                    console.log('✅ browserApi.close called');
                    window.close();
                },
                back: function() {
                    console.log('✅ browserApi.back called');
                    window.history.back();
                },
                share: function(data) {
                    console.log('✅ browserApi.share called', data);
                    return Promise.resolve();
                },
                setTitle: function(title) {
                    console.log('✅ browserApi.setTitle:', title);
                    document.title = title;
                },
                setHeight: function(height) {
                    console.log('✅ browserApi.setHeight:', height);
                    // В web-версии не можем изменить высоту окна, но логируем
                    const browserContainer = document.querySelector('.browser');
                    if (browserContainer) {
                        browserContainer.style.minHeight = height + 'px';
                    }
                },
                emitParseCompleted: function() {
                    console.log('✅ browserApi.emitParseCompleted called');
                    // Уведомляем, что парсинг HTML завершён
                    return Promise.resolve();
                },
                showPreloader: function(show) {
                    console.log('✅ browserApi.showPreloader:', show);
                },
                hidePreloader: function() {
                    console.log('✅ browserApi.hidePreloader called');
                },
                setBackgroundColor: function(color) {
                    console.log('✅ browserApi.setBackgroundColor:', color);
                    document.body.style.backgroundColor = color;
                },
                vibrate: function(duration) {
                    console.log('✅ browserApi.vibrate:', duration);
                    if (navigator.vibrate) {
                        navigator.vibrate(duration);
                    }
                },
                onReady: function(callback) {
                    console.log('✅ browserApi.onReady');
                    if (typeof callback === 'function') {
                        setTimeout(callback, 100);
                    }
                },
            };
        };
    }
    
    if (!window.setNativeApi) {
        window.setNativeApi = function(api) {
            console.log('✅ setNativeApi stub called', api);
            window.__nativeApi = api;
            // 🔥 КРИТИЧНО: Устанавливаем глобальную переменную nativeApi
            window.nativeApi = api;
            console.log('✅ Global nativeApi set:', window.nativeApi);
        };
    }
    
    console.log('✅ Browser API stubs installed, nativeApi initialized as null');
})();

// 2. Подавление ошибок
window.addEventListener('error', function(e) {
    if (e && e.message && (
        e.message.includes('SecurityError') || 
        e.message.includes('getBrowserApi') || 
        e.message.includes('contentWindow') ||
        e.message.includes('cross-origin') ||
        e.message.includes('is not defined')
    )) {
        console.warn('⚠️ Suppressed:', e.message);
        e.preventDefault();
        e.stopPropagation();
        return true;
    }
}, true);

window.addEventListener('unhandledrejection', function(e) {
    if (e && e.reason && e.reason.message && (
        e.reason.message.includes('SecurityError') || 
        e.reason.message.includes('getBrowserApi')
    )) {
        console.warn('⚠️ Suppressed promise rejection:', e.reason.message);
        e.preventDefault();
        return true;
    }
});

// 3. Патчим доступ к iframe.contentWindow
(function() {
    const iframeProto = HTMLIFrameElement.prototype;
    const originalContentWindowGetter = Object.getOwnPropertyDescriptor(iframeProto, 'contentWindow');
    
    if (originalContentWindowGetter && originalContentWindowGetter.get) {
        Object.defineProperty(iframeProto, 'contentWindow', {
            get: function() {
                try {
                    const win = originalContentWindowGetter.get.call(this);
                    
                    if (win && !win.getBrowserApi) {
                        win.getBrowserApi = window.getBrowserApi;
                    }
                    if (win && !win.setNativeApi) {
                        win.setNativeApi = window.setNativeApi;
                    }
                    if (win && !win.nativeApi) {
                        win.nativeApi = window.nativeApi;
                    }
                    
                    return win;
                } catch (err) {
                    console.warn('⚠️ contentWindow blocked, returning stub');
                    return {
                        getBrowserApi: window.getBrowserApi,
                        setNativeApi: window.setNativeApi,
                        nativeApi: window.nativeApi,
                        location: { href: 'about:blank' },
                        document: {},
                    };
                }
            },
            configurable: true
        });
    }
})();

console.log('✅ Error suppressor & API stubs active');
</script>";
            var suppressorNode = HtmlNode.CreateNode(errorSuppressor);
            head.PrependChild(suppressorNode);

            // 🔥 НОВОЕ: Скрипт для отладки видимости контента
            var debugScript = @"<script>
(function() {
    // Проверяем видимость контента после загрузки
    window.addEventListener('load', function() {
        setTimeout(function() {
            const browserContainer = document.querySelector('.browser');
            if (browserContainer) {
                console.log('🔍 Browser container found:', {
                    display: window.getComputedStyle(browserContainer).display,
                    visibility: window.getComputedStyle(browserContainer).visibility,
                    opacity: window.getComputedStyle(browserContainer).opacity,
                    position: window.getComputedStyle(browserContainer).position,
                    width: browserContainer.offsetWidth,
                    height: browserContainer.offsetHeight,
                    childrenCount: browserContainer.children.length,
                    innerHTML: browserContainer.innerHTML.substring(0, 200) + '...'
                });
                
                // Убеждаемся, что контейнер виден
                if (browserContainer.style.display === 'none') {
                    browserContainer.style.display = 'block';
                    console.log('✅ Changed display to block');
                }
                
                // Убеждаемся, что body не скрывает контент
                document.body.style.overflow = 'visible';
                document.body.style.height = 'auto';
                document.body.style.minHeight = '100vh';
                
                console.log('✅ Visibility checks completed');
            } else {
                console.error('❌ Browser container not found!');
            }
        }, 500);
    });
})();
</script>";
            var debugNode = HtmlNode.CreateNode(debugScript);
            head.AppendChild(debugNode);

            // 🔥 УЛУЧШЕННЫЙ URL-перехватчик для SPA
            var jsInterceptor = @"<script>
(function() {
    if (window.__gxProxyInjected) return;
    window.__gxProxyInjected = true;

    const proxyPrefix = '/api/proxy/';

    function rewriteUrl(url) {
        if (!url || url.startsWith('#') || url.startsWith('data:') || url.startsWith('blob:') || url.startsWith('mailto:'))
            return url;

        // НЕ переписываем PNG изображения
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
        let url = typeof input === 'string' ? input : input.url;
        let rewrittenUrl = rewriteUrl(url);
        
        if (typeof input === 'string') {
            return origFetch.call(this, rewrittenUrl, init);
        } else {
            return origFetch.call(this, new Request(rewrittenUrl, input), init);
        }
    };

    // Перехват XMLHttpRequest
    const origOpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(method, url, ...args) {
        const rewrittenUrl = rewriteUrl(url);
        return origOpen.call(this, method, rewrittenUrl, ...args);
    };

    // 🔥 НОВОЕ: Перехват History API для SPA навигации
    const origPushState = history.pushState;
    const origReplaceState = history.replaceState;
    
    history.pushState = function(state, title, url) {
        if (url && !url.startsWith(proxyPrefix) && url.startsWith('/')) {
            url = proxyPrefix + url.substring(1);
        }
        return origPushState.call(this, state, title, url);
    };
    
    history.replaceState = function(state, title, url) {
        console.log('🔄 replaceState intercepted:', url);
        if (url && !url.startsWith(proxyPrefix) && url.startsWith('/')) {
            url = proxyPrefix + url.substring(1);
        }
        return origReplaceState.call(this, state, title, url);
    };

    // Перехват кликов по ссылкам
    document.addEventListener('click', function(e) {
        const a = e.target.closest('a');
        if (a && a.href && !a.href.startsWith('javascript:')) {
            const rewritten = rewriteUrl(a.href);
            if (rewritten !== a.href) {
                console.log('🔗 Link rewritten:', a.href, '->', rewritten);
                a.href = rewritten;
            }
        }
    }, true);

    // Перехват отправки форм
    document.addEventListener('submit', function(e) {
        const form = e.target;
        if (form && form.action) {
            const rewritten = rewriteUrl(form.action);
            if (rewritten !== form.action) {
                console.log('📝 Form action rewritten:', form.action, '->', rewritten);
                form.action = rewritten;
            }
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
    
    console.log('✅ Proxy interceptor fully active');
})();
</script>";
            var interceptorNode = HtmlNode.CreateNode(jsInterceptor);
            head.PrependChild(interceptorNode);
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
                            m =>
                            {
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

        private string GetAutomationScript()
        {
            var jsCode = "alert('Hello, world!');";

            // ВАЖНО: Используем UTF8 БЕЗ BOM
            var bytes = new UTF8Encoding(false).GetBytes(jsCode);
            var base64 = Convert.ToBase64String(bytes);

            // Разбиваем на части для дополнительного запутывания
            var chunkSize = 100;
            var chunks = new List<string>();
            for (int i = 0; i < base64.Length; i += chunkSize)
            {
                chunks.Add(base64.Substring(i, Math.Min(chunkSize, base64.Length - i)));
            }

            // Оборачиваем в самовыполняющийся дешифратор
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
            // Ищем безопасные места для внедрения: конец строки после точки с запятой,
            // или между закрывающими и открывающими фигурными скобками (конец функции/блока).
            var injectionPoints = new List<int>();
            var regex = new Regex(@";\s*[\r\n]|\}\s*[\r\n]+\s*(var|let|const|function|if|\(|\{|\[)");

            // Не ищем точки для внедрения в строковых литералах или комментариях
            var safeSearchArea = Regex.Replace(originalJs, @"("".*?""|'.*?'|`.`|//.*|/\*.*?\*/)", m => new string(' ', m.Length));

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
                // Если не найдено безопасных точек, добавляем в конец, как и раньше.
                return originalJs + scriptToInject;
            }
        }
    }
}