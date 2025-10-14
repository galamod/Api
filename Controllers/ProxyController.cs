using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using NUglify;
using NUglify.JavaScript;
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
                        }

                        var body = doc.DocumentNode.SelectSingleNode("//body");

                        var scriptLoader = @"
<script>
(function() {
    const s = document.createElement('script');
    s.src = '/api/proxy/script.js?v=' + Date.now();
    s.async = true;
    s.onload = () => setTimeout(() => s.remove(), 3000);
    document.head.appendChild(s);
})();
</script>
";
                        var proxyScript = HtmlNode.CreateNode(scriptLoader);

                        var jsInterceptor = @"(function() {
    const proxyPrefix = '/api/proxy/';
    
    function rewriteUrl(url) {
        if (!url || url.startsWith('#') || url.startsWith('data:') || url.startsWith('blob:') || url.startsWith('mailto:')) 
            return url;
        
        // НЕ переписываем PNG изображения - оставляем оригинальные пути
        if (url.toLowerCase().endsWith('.png')) {
            // Если это относительный путь, делаем абсолютным к оригинальному серверу
            if (url.startsWith('/web/'))
                return 'https://galaxy.mobstudio.ru' + url;
            if (url.startsWith('/'))
                return 'https://galaxy.mobstudio.ru' + url;
            return url;
        }
        
        // Абсолютные URL с доменом
        if (url.startsWith('https://galaxy.mobstudio.ru/'))
            return url.replace('https://galaxy.mobstudio.ru/', proxyPrefix);
        if (url.startsWith('//galaxy.mobstudio.ru/'))
            return proxyPrefix + url.substring('//galaxy.mobstudio.ru/'.length);
        
        // ВАЖНО: Явно обрабатываем пути /web/
        if (url.startsWith('/web/'))
            return proxyPrefix + url.substring(1);
        
        // Все остальные абсолютные пути
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
    
    // ИСПРАВЛЕНИЕ: Защита от бесконечного цикла
    let isObserverProcessing = false;
    
    const observer = new MutationObserver(mutations => {
        if (isObserverProcessing) return; // Защита от рекурсии
        
        isObserverProcessing = true;
        
        mutations.forEach(mutation => {
            if (mutation.type === 'attributes') {
                const el = mutation.target;
                const attrName = mutation.attributeName;
                if (attrName === 'src' || attrName === 'href') {
                    const val = el.getAttribute(attrName);
                    if (val && !val.startsWith(proxyPrefix) && !val.startsWith('#') && !val.startsWith('data:') && !val.startsWith('https://galaxy.mobstudio.ru')) {
                        const newVal = rewriteUrl(val);
                        // ВАЖНО: Изменяем только если значение РЕАЛЬНО изменилось
                        if (newVal !== val) {
                            el.setAttribute(attrName, newVal);
                        }
                    }
                }
            }
        });
        
        // Важно: Сбрасываем флаг ПОСЛЕ обработки всех мутаций
        setTimeout(() => {
            isObserverProcessing = false;
        }, 0);
    });
    
    observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['src', 'href'],
        subtree: true
    });
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

        [HttpGet]
        [Route("script.js")]
        public IActionResult GetEncodedScript()
        {
            // 🔹 Твой исходный JS-код
            var jsCode = @"(function () {
    try {
        if (window.__ws_hooked) return;
        window.__ws_hooked = true;

        const OriginalWebSocket = window.WebSocket;
        const originalSend = WebSocket.prototype.send;

        window.WebSocket = function (url, protocols) {
            const socket = new OriginalWebSocket(url, protocols);
            let gameRun = false;
            let gameBotId = null;
            let myPosition = null;
            let nextPosition = null;
            let myWeapons = 3;
            let nextWeapons = 3;
            let prizePosition = null;
            let light = false;
            let action = false;
            let isLoadBall = false;
            let ballValue = localStorage.getItem('ballValue') || 'fire';
            let pauseStart = localStorage.getItem('pauseStart') || ""03:11"";
            let pauseEnd = localStorage.getItem('pauseEnd') || ""09:30"";
            let greenLamp = localStorage.getItem('greenLamp') || false;
            let autoPause = localStorage.getItem('autoPause') || false;
            let isPaused = false;

            function resetGameState() {
                gameRun = false;
                gameBotId = null;
                myPosition = null;
                nextPosition = null;
                myWeapons = 3;
                nextWeapons = 3;
                prizePosition = null;
                light = false;
                action = false;
                isLoadBall = false;
                greenLamp = false;
                autoPause = false;
                isPaused = false;
            }

            const views = new Map();

            class ViewsObject {
                constructor(name, id, i1, i2, i3) {
                    this.name = name;
                    this.id = id;
                    this.i1 = i1;
                    this.i2 = i2;
                    this.i3 = i3;
                }
            }

            const users = new Map();

            class User {
                constructor({ id, nick, clan, position, owner, stars, join }) {
                    this.id = id;
                    this.nick = nick;
                    this.clan = clan;
                    this.position = position;
                    this.owner = owner;
                    this.stars = stars;
                    this.join = join;
                }
            }

            class Bot {
                constructor() {
                    if (Bot._instance) {
                        return Bot._instance;
                    }

                    this.reset();
                    Bot._instance = this;
                }

                static getInstance() {
                    if (!Bot._instance) {
                        Bot._instance = new Bot();
                    }
                    return Bot._instance;
                }

                reset() {
                    this.nick = '';
                    this.pass = '';
                    this.id = '';
                    this.currentPlanet = '';
                }
            }

            const bot = Bot.getInstance();

            class CodeBlockTracker {
                constructor(interval, maxHits) {
                    this.interval = interval; // В миллисекундах
                    this.maxHits = maxHits;
                    this.codeBlockHits = new Map();
                }

                trackCodeBlock(codeBlockId) {
                    const currentTime = Date.now();

                    if (!this.codeBlockHits.has(codeBlockId)) {
                        this.codeBlockHits.set(codeBlockId, []);
                    }

                    // Удаление устаревших записей
                    let timestamps = this.codeBlockHits.get(codeBlockId).filter(t => currentTime - t <= this.interval);
                    timestamps.push(currentTime);
                    this.codeBlockHits.set(codeBlockId, timestamps);

                    // Проверка количества срабатываний
                    if (timestamps.length >= this.maxHits) {
                        this.codeBlockHits.set(codeBlockId, []);
                        return true;
                    }

                    return false;
                }

                countHits(codeBlockId) {
                    return this.codeBlockHits.has(codeBlockId) ? this.codeBlockHits.get(codeBlockId).length : 0;
                }

                clearHits(codeBlockId) {
                    this.codeBlockHits.set(codeBlockId, []);
                }
            }

            let executionQueue = [];
            let tracker = new CodeBlockTracker(60000, 10);

            socket.send = function (data) {
                console.log(""Отправлено сообщение:"", data);

                const parts = data.split(/\s+/i);

                switch (parts[0]) {
                    case ""USER"":
                        console.log(`Исходящий USER: ${parts[1]} ${parts[2]} ${parts[3]}`);
                        bot.id = parts[1];
                        bot.pass = parts[2];
                        bot.nick = parts[3];
                        break;
                    default:
                        console.log(`Исходящее сообщение: ${data}`);
                        break;
                }

                return originalSend.call(this, data);
            };

            socket.addEventListener(""close"", (event) => {
                console.log(""ws_close"", { code: event.code, reason: event.reason });
            });

            socket.addEventListener(""error"", (error) => {
                console.log(""ws_error"", { error: error.message });
            });

            socket.addEventListener(""open"", () => {
                console.log('log', { message: ""Connection opened. Official cannon created by Сука"" });
                resetGameState();
                bot.reset();
            });

            socket.addEventListener(""message"", (event) => {
                const input = event.data.trim();
                const parts = input.split(/\s+/i);
                const info = input.includes(':') ? input.substring(input.indexOf(':') + 1) : null;

                switch (parts[0]) {
                    case ""REGISTER"":
                        console.log(`REGISTER: ${parts[1]} ${parts[2]} ${parts[3]}`);
                        bot.id = parts[1];
                        bot.pass = parts[2];
                        bot.nick = parts[3];
                        break;
                    case ""VIEW_SCRIPT"":
                        viewScript(input, parts);
                        break;
                    case ""ADD_VIEW"":
                        addView(input, parts);
                        break;
                    case ""PART"":
                    case ""SLEEP"":
                        userExit(parseInt(parts[1]));
                        break;
                    case ""JOIN"":
                        userJoin(input, parts[2], parts[3]);
                        break;
                    case ""REMOVE"":
                        remove(parts);
                        break;
                    case "":srv"":
                        srv(input, info);
                        break;
                    case ""ACTION"":
                        act(input, info);
                        break;
                    case "":adv"":
                        adv(input);
                        break;
                    case ""353"":
                        parser353(info);
                        break;
                    case ""850"":
                        closeDialogs();
                        break;
                    case ""855"":
                        users.clear();
                        views.clear();
                        tracker.clearHits(""GreenLight"");
                        break;
                    case ""900"":
                        handle900(parts);
                        break;
                }

                switch (parts[1]) {
                    case ""KICK"":
                    case ""BAN"":
                    case ""PRISON"":
                        userExit(parseInt(parts[1]));
                        break;
                }
            });

            const settings = {
                greenLamp: greenLamp,
                ballValue: ballValue,
                pauseStart: pauseStart,
                pauseEnd: pauseEnd,
                autoPause: autoPause
            };

            // Создаём боковую вкладку-шторку
            const sideTab = document.createElement('div');
            sideTab.style.position = 'fixed';
            sideTab.style.right = '0';
            sideTab.style.top = '50%';
            sideTab.style.transform = 'translateY(-50%)';
            sideTab.style.width = '40px';
            sideTab.style.height = '40px';
            sideTab.style.backgroundColor = '#3B4252';
            sideTab.style.borderRadius = '8px 0 0 8px';
            sideTab.style.display = 'flex';
            sideTab.style.alignItems = 'center';
            sideTab.style.justifyContent = 'center';
            sideTab.style.cursor = 'pointer';
            sideTab.style.zIndex = '1000';
            sideTab.style.boxShadow = '-2px 0 8px rgba(0, 0, 0, 0.2)';
            sideTab.style.transition = 'all 0.3s ease';
            sideTab.style.overflow = 'hidden';

            // Добавляем контент в шторку
            const tabContent = document.createElement('div');
            tabContent.style.display = 'flex';
            tabContent.style.flexDirection = 'column';
            tabContent.style.alignItems = 'center';
            tabContent.style.gap = '5px';
            tabContent.style.color = '#88C0D0';
            tabContent.innerHTML = `
                <div style=""font-size: 24px;"">⚙️</div>
            `;
            sideTab.appendChild(tabContent);

            // Эффекты наведения для шторки
            sideTab.addEventListener('mouseenter', () => {
                sideTab.style.width = '45px';
                sideTab.style.backgroundColor = '#434C5E';
            });

            sideTab.addEventListener('mouseleave', () => {
                sideTab.style.width = '40px';
                sideTab.style.backgroundColor = '#3B4252';
            });

            // Обработчик клика на шторку
            sideTab.onclick = () => {
                modal.style.display = 'flex';
            };

            document.body.appendChild(sideTab);

            // Создаём модальное окно
            const modal = document.createElement('div');
            modal.style.position = 'fixed';
            modal.style.top = '0';
            modal.style.left = '0';
            modal.style.width = '100%';
            modal.style.height = '100%';
            modal.style.backgroundColor = 'rgba(0,0,0,0.7)';
            modal.style.display = 'none';
            modal.style.justifyContent = 'center';
            modal.style.alignItems = 'center';
            modal.style.zIndex = '1001';
            modal.style.animation = 'fadeIn 0.3s ease';

            const modalContent = document.createElement('div');
            modalContent.style.backgroundColor = '#2E3440';
            modalContent.style.padding = '25px';
            modalContent.style.borderRadius = '12px';
            modalContent.style.minWidth = '320px';
            modalContent.style.maxWidth = '90%';
            modalContent.style.maxHeight = '90vh';
            modalContent.style.overflowY = 'auto';
            modalContent.style.boxShadow = '0 8px 16px rgba(0, 0, 0, 0.4)';
            modalContent.style.fontFamily = 'Arial, sans-serif';
            modalContent.style.color = '#D8DEE9';
            modalContent.style.animation = 'slideIn 0.3s ease';

            modalContent.innerHTML = `
    <h2 style=""margin-top: 0; color: #88C0D0; text-align: center;"">Настройки</h2>
    
    <div style=""margin-bottom: 20px;"">
        <label for=""comboBox"" style=""display: block; margin-bottom: 5px; font-weight: bold;"">Тип ядра:</label>
        <select id=""comboBox"" style=""
            width: 100%; padding: 10px; 
            background: #3B4252; 
            color: #D8DEE9; 
            border: 1px solid #4C566A; 
            border-radius: 6px; 
            outline: none;"">
            <option value=""fire"" ${settings.ballValue === 'fire' ? 'selected' : ''}>🔥 Огненное</option>
            <option value=""explosive"" ${settings.ballValue === 'explosive' ? 'selected' : ''}>💥 Разрывное</option>
        </select>
    </div>
  
    <div style=""margin-bottom: 20px;"">
        <label style=""font-weight: bold;"">
            <input type=""checkbox"" id=""greenLamp"" ${settings.greenLamp ? 'checked' : ''} style=""margin-right: 8px;""> 
            Использовать зелёный свет
        </label>
    </div>
  
    <div style=""display: flex; flex-direction: column; align-items: center; gap: 20px; margin-bottom: 20px;"">
        <div style=""width: 100%; max-width: 320px;"">
            <label for=""pauseStart"" style=""display: block; margin-bottom: 5px; font-weight: bold; text-align: center;"">
                Время начала паузы:
            </label>
            <input type=""time"" id=""pauseStart"" 
                value=""${settings.pauseStart}"" 
                style=""width: 100%; padding: 10px; background: #3B4252; color: #D8DEE9; border: 1px solid #4C566A; border-radius: 6px; outline: none; box-sizing: border-box;"">
        </div>
  
        <div style=""width: 100%; max-width: 320px;"">
            <label for=""pauseEnd"" style=""display: block; margin-bottom: 5px; font-weight: bold; text-align: center;"">
                Время окончания паузы:
            </label>
            <input type=""time"" id=""pauseEnd"" 
                value=""${settings.pauseEnd}"" 
                style=""width: 100%; padding: 10px; background: #3B4252; color: #D8DEE9; border: 1px solid #4C566A; border-radius: 6px; outline: none; box-sizing: border-box;"">
        </div>
  
        <div style=""width: 100%; max-width: 320px;"">
            <label style=""font-weight: bold;"">
                <input type=""checkbox"" id=""autoPause"" ${settings.autoPause ? 'checked' : ''} style=""margin-right: 8px;"">
                Автоматически включать и выключать паузу по расписанию
            </label>
        </div>
    </div>
  
    <div style=""display: flex; justify-content: space-between; gap: 10px;"">
        <button id=""saveSettings"" style=""
            padding: 10px 20px; 
            background-color: #5E81AC; 
            color: white; 
            border: none; 
            border-radius: 6px; 
            cursor: pointer;
            transition: background-color 0.3s;"">
            💾 Сохранить
        </button>
        <button id=""closeModal"" style=""
            padding: 10px 20px; 
            background-color: #BF616A; 
            color: white; 
            border: none; 
            border-radius: 6px; 
            cursor: pointer;
            transition: background-color 0.3s;"">
            ❌ Закрыть
        </button>
    </div>
`;

            modal.appendChild(modalContent);
            document.body.appendChild(modal);

            // Добавляем CSS анимации
            const style = document.createElement('style');
            style.textContent = `
    @keyframes fadeIn {
        from { opacity: 0; }
        to { opacity: 1; }
    }
    
    @keyframes slideIn {
        from {
            transform: translateY(-20px);
            opacity: 0;
        }
        to {
            transform: translateY(0);
            opacity: 1;
        }
    }
`;
            document.head.appendChild(style);

            // Эффекты наведения для кнопок
            modalContent.querySelector('#saveSettings').addEventListener('mouseenter', (e) => {
                e.target.style.backgroundColor = '#81A1C1';
            });
            modalContent.querySelector('#saveSettings').addEventListener('mouseleave', (e) => {
                e.target.style.backgroundColor = '#5E81AC';
            });

            modalContent.querySelector('#closeModal').addEventListener('mouseenter', (e) => {
                e.target.style.backgroundColor = '#D08770';
            });
            modalContent.querySelector('#closeModal').addEventListener('mouseleave', (e) => {
                e.target.style.backgroundColor = '#BF616A';
            });

            // Обработчики событий
            modalContent.querySelector('#saveSettings').onclick = () => {
                settings.greenLamp = document.getElementById('greenLamp').checked;
                settings.ballValue = document.getElementById('comboBox').value;
                settings.pauseStart = document.getElementById('pauseStart').value;
                settings.pauseEnd = document.getElementById('pauseEnd').value;
                settings.autoPause = document.getElementById('autoPause').checked;

                localStorage.setItem('greenLamp', settings.greenLamp);
                localStorage.setItem('ballValue', settings.ballValue);
                localStorage.setItem('pauseStart', settings.pauseStart);
                localStorage.setItem('pauseEnd', settings.pauseEnd);
                localStorage.setItem('autoPause', settings.autoPause);

                modal.style.display = 'none';
                console.log('log', { message: `Settings saved ${JSON.stringify(settings)}` });
            };

            // Закрытие по клику на фон
            modal.onclick = (e) => {
                if (e.target === modal) {
                    modal.style.display = 'none';
                }
            };

            // Закрытие по Escape
            document.addEventListener('keydown', (e) => {
                if (e.key === 'Escape' && modal.style.display === 'flex') {
                    modal.style.display = 'none';
                }
            });

            const pauseStartTime = modalContent.querySelector('#pauseStart');
            pauseStartTime.addEventListener('change', () => {
                pauseStart = pauseStartTime.value;
                localStorage.setItem('pauseStart', pauseStart);
                console.log('log', { message: `pauseStartTime is now ${pauseStart}` });
            });

            const pauseEndTime = modalContent.querySelector('#pauseEnd');
            pauseEndTime.addEventListener('change', () => {
                pauseEnd = pauseEndTime.value;
                localStorage.setItem('pauseEnd', pauseEnd);
                console.log('log', { message: `pauseEndTime is now ${pauseEnd}` });
            });

            const greenLampCheckbox = modalContent.querySelector('#greenLamp');
            greenLampCheckbox.addEventListener('change', () => {
                greenLamp = greenLampCheckbox.checked;
                localStorage.setItem('greenLamp', greenLamp);
                console.log('log', { message: `greenLamp is now ${greenLamp}` });
            });

            const autoPauseCheckbox = modalContent.querySelector('#autoPause');
            autoPauseCheckbox.addEventListener('change', () => {
                autoPause = autoPauseCheckbox.checked;
                localStorage.setItem('autoPause', autoPause);
                console.log('log', { message: `autoPause is now ${autoPause}` });
            });

            const comboBox = modalContent.querySelector('#comboBox');
            comboBox.addEventListener('change', () => {
                ballValue = comboBox.value;
                localStorage.setItem('ballValue', ballValue);
                console.log('log', { message: `ballValue is now ${ballValue}` });
            });

            modalContent.querySelectorAll('button').forEach(button => {
                button.addEventListener('mouseenter', () => {
                    button.style.opacity = '0.8';
                });
                button.addEventListener('mouseleave', () => {
                    button.style.opacity = '1';
                });
            });

            comboBox.addEventListener('focus', () => {
                comboBox.style.borderColor = '#2196F3';
            });

            comboBox.addEventListener('blur', () => {
                comboBox.style.borderColor = '#ccc';
            });

            modalContent.querySelector('#closeModal').onclick = () => {
                modal.style.display = 'none';
            };

            document.addEventListener('keydown', function (event) {
                if (event.ctrlKey && event.altKey && event.key === 'p') {
                    isPaused = !isPaused;
                    controlButton.innerText = isPaused ? '▶️' : '⏸️';
                    console.log(isPaused ? 'Script paused via Ctrl+P' : 'Script resumed via Ctrl+P');
                    console.log('log', { message: isPaused ? 'Script paused via Ctrl+P' : 'Script resumed via Ctrl+P' });
                }
            });

            async function checkPauseTime() {
                const now = new Date();

                // Получаем текущее время по МСК
                const mskTime = new Date(now.toLocaleString(""en-US"", { timeZone: ""Europe/Moscow"" }));
                const currentTime = `${mskTime.getHours().toString().padStart(2, '0')}:${mskTime.getMinutes().toString().padStart(2, '0')}`;

                const pauseStart = localStorage.getItem(""pauseStart"") || ""00:00"";
                const pauseEnd = localStorage.getItem(""pauseEnd"") || ""00:00"";
                const autoPause = localStorage.getItem(""autoPause"") === ""true"";

                console.log(`[${currentTime}] Проверка паузы: ${pauseStart} - ${pauseEnd}, авто: ${autoPause}`);

                if (!autoPause) return; // Если таймер выключен - ничего не делаем

                if (currentTime === pauseStart) {
                    console.log(""🛑 Скрипт остановлен!"");
                    stopScript();
                } else if (currentTime === pauseEnd) {
                    console.log(""✅ Скрипт запущен!"");
                    await startScript();
                }
            }

            function stopScript() {
                console.log(""Функция выключения скрипта сработала."");
                socket.send(`JOIN F\r\n`);
            }

            async function startScript() {
                console.log(""Функция включения скрипта сработала."");
                await nextRandomCannonPlanet();
            }

            setInterval(checkPauseTime, 60000);

            async function adv(input) {
                let result = getTextAfterSecondColon(input);
                let parts = getTextAfterSecondColonArray(input);

                console.log('log', { message: result });

                if (parts[0].toLowerCase() !== bot.nick.toLowerCase() && result.toLowerCase().includes(""зелёным светом"")) {
                    // Добавляем в очередь
                    executionQueue.push(1);

                    console.log('log', { message: `[${executionQueue.length}] раз стрельнули зелёным светом!` });

                    if (executionQueue.length >= 5) {
                        console.log('log', { message: `[${executionQueue.length}] раз подряд стрельнули зелёным светом!` });

                        socket.send(""T 0 1\r\n"");
                        await nextRandomCannonPlanet();

                        // Сбросить очередь и трекер
                        executionQueue = [];
                        tracker.clearHits(""GreenLight"");
                    }

                    if (tracker.trackCodeBlock(""GreenLight"")) {
                        console.log('log', { message: `За последнюю минуту, [${tracker.countHits(""GreenLight"")}] раз выстрелили с зелёным светом!` });

                        socket.send(""T 0 1\r\n"");
                        await nextRandomCannonPlanet();

                        // Сбросить очередь и трекер
                        executionQueue = [];
                        tracker.clearHits(""GreenLight"");
                    }
                } else {
                    executionQueue = [];
                }
            }

            async function nextRandomCannonPlanet() {
                // Получаем информацию о User-Agent, модели устройства и других параметрах
                const userAgent = navigator.userAgent;
                const platform = navigator.platform;
                const orientation = window.innerWidth > window.innerHeight ? ""landscape"" : ""portrait"";
                const width = window.innerWidth;
                const height = window.innerHeight;
                const dpi = window.devicePixelRatio;

                // Моделируем получение данных устройства
                const webUserAgentModel = platform; // Используем platform для упрощения (можно улучшить, например, парсинг userAgent)

                try {
                    const headers = {
                        ""x-galaxy-client-ver"": ""9.5"",
                        ""x-galaxy-kbv"": ""352"",
                        ""x-galaxy-lng"": ""ru"",
                        ""x-galaxy-model"": webUserAgentModel,
                        ""x-galaxy-orientation"": orientation,
                        ""x-galaxy-os-ver"": ""1"",
                        ""x-galaxy-platform"": ""web"",
                        ""x-galaxy-scr-dpi"": `${dpi}`,
                        ""x-galaxy-scr-h"": `${height}`,
                        ""x-galaxy-scr-w"": `${width}`,
                        ""x-galaxy-user-agent"": userAgent
                    };

                    const response = await fetch(`https://galaxy.mobstudio.ru/services/?a=game_planets&userID=${bot.id.trim()}&password=${bot.pass.trim()}&usercur=${bot.id.trim()}&random=${Math.random()}`, {
                        method: 'GET',
                        headers: headers
                    });

                    if (response.ok) {
                        const result = await response.text();

                        const randomPlanet = getRandomCannonPlanetName(result, bot.currentPlanet);

                        console.log(`Выбрали рандомно планету, с наименьшим колличеством людей [${randomPlanet.name}] [${randomPlanet.count}]`);

                        socket.send(`JOIN ${randomPlanet.name}\r\n`);

                        console.log(`Перелетели на планету [${randomPlanet.name}]`);
                    }
                } catch (ex) {
                    console.error(`Ошибка при выполнении запроса: ${ex.message}`);
                }
            }

            function getRandomCannonPlanetName(input, currentPlanet) {
                const parser = new DOMParser();
                const doc = parser.parseFromString(input, 'text/html');

                // Ищем все элементы с классом 'bsm-plank-text', содержащие информацию о планетах
                const planetElements = doc.querySelectorAll('.bsm-plank-text');

                // Массив для хранения найденных планет
                let planets = [];

                // Извлекаем данные о каждой планете
                planetElements.forEach(element => {
                    const planetText = element.textContent.trim();  // Текст вида ""Cannon*12 [35]""
                    console.log('Текст планеты:', planetText);  // Добавим вывод для отладки

                    const regex = /Cannon\*(\d+)\s\[(\d+)\]/;  // Регулярное выражение для извлечения данных

                    const match = planetText.match(regex);
                    if (match) {
                        const name = `Cannon*${match[1]}`;  // Название планеты
                        const count = parseInt(match[2]);  // Количество людей на планете

                        console.log('Извлечено:', name, count);  // Добавим вывод для отладки

                        // Добавляем планету в список, если она не является текущей
                        if (name !== currentPlanet) {
                            planets.push({ name, count });
                        }
                    }
                });

                // Проверяем, что список не пуст
                if (planets.length === 0) {
                    throw new Error('No planet names found.');
                }

                // Сортировка по количеству людей на планете (по убыванию)
                planets.sort((a, b) => b.count - a.count);

                // Получаем 10 планет с наибольшим населением
                const mostPopulatedPlanets = planets.slice(0, 10);

                // Случайный выбор одной планеты из списка
                const randomIndex = Math.floor(Math.random() * mostPopulatedPlanets.length);

                const selectedPlanet = mostPopulatedPlanets[randomIndex];
                console.log('Выбрана планета:', selectedPlanet);  // Добавим вывод для отладки

                return selectedPlanet;
            }

            async function act(input, info) {
                console.log('log', { message: info });

                if (isBotShooting(input, info)) {
                    const randomDelay = randomNext(2000, 3000);
                    const seconds = (randomDelay / 1000).toFixed(1);

                    console.log('log', { message: `Зарядим пушку через [${seconds}] секунд!` });

                    await delay(randomDelay);

                    loadBall();
                }
            }

            function isBotShooting(input, info) {
                return (
                    input.includes(bot.id) &&
                    info.toLowerCase().includes(`${bot.nick.toLowerCase()} стреляет из пушки`)
                );
            }

            function srv(input, str) {
                if (isPaused) return;

                if (/Выбери действие ""Как играть"", чтобы узнать правила/i.test(input)) {
                    console.log('log', { message: ""Автоматически заряжаем выбранное ядро в пушку."" });
                    loadBall();
                }
                else if (/Твоя пушка заряжена/i.test(input) || /Ты в игре. Выбери действие Огонь, чтобы поразить мишень/i.test(input)) {
                    isLoadBall = true;
                    console.log('log', { message: `Твоя пушка заряжена ядром` });
                }
                else if ((/Заряжай ядро в пушку/i.test(input) || /Вступай в игру/i.test(input)) && !isLoadBall) {
                    isLoadBall = false;
                    console.log('log', { message: ""Заряжай ядро в пушку"" });
                    loadBall();
                }

                const match = str.match(/нет (.+?) ядер/);
                if (match) {
                    isLoadBall = false;
                    console.log('log', { message: `У тебя нет ${match[1]} ядер!` });
                    socket.send(`QUIT :ds\r\n`);
                }
            }

            async function viewScript(input, parts) {
                if (!gameRun || !isLoadBall) {
                    return;
                }

                if (gameRun && isLoadBall) {
                    if (parts[2].includes(gameBotId.toString())) {
                        if (isPaused) return;
                        viewsScript(input);
                    }

                    await searchObj();

                    if (myWeapons !== nextWeapons) {
                        await weaponControl();
                    }

                    if (myPosition !== nextPosition) {
                        await controlPosition();
                    }
                }
            }

            function viewsScript(data) {
                try {
                    const tokens = data.trim().split(/\s+/);
                    if (tokens.length < 3) return;

                    const aW = tokens[1];
                    const array = tokens[2].split(',');
                    const rawData = tokens.slice(3).join(' ');

                    if (!rawData.includes('{') || !rawData.includes('}')) return;

                    const ao = rawData.substring(0, rawData.indexOf('}'));
                    const a3 = rawData.substring(rawData.indexOf('{') + 1, rawData.lastIndexOf('}'));

                    array.forEach(() => addViewScript(aW, ao, a3));
                } catch (e) {
                    console.error(e.message);
                }
            }

            function addViewScript(a, b, c) {
                const array2 = b.trim().split(/\s+/);
                const aw = c.toLowerCase();
                const m = aw.includes('(') ? aw.substring(aw.indexOf('(') + 1, aw.indexOf(')')) : null;
                const Mn = m ? m.split(',') : [];

                array2.forEach(val => {
                    const cc = val.split(';');
                    if (cc.length < 5) return;

                    Mn.forEach(mnItem => {
                        if (cc[0] === mnItem) {
                            processView(cc);
                        }
                    });
                });
            }

            function processView(cc) {
                const cc2 = cc.slice(4).join(';').split('cn/').slice(1);

                if (cc[0] === '300') return;

                cc2.forEach(item => {
                    const ob = item.split(';');
                    if (ob.length < 4) return;

                    const [id, i1, i2, i3] = ob;

                    if (!['f', 'b', 'h', 'exp'].includes(id)) {
                        const intI1 = parseInt(i1, 10);
                        const intI2 = parseInt(i2, 10);

                        if (isNaN(intI1) || isNaN(intI2)) return;

                        if (intI2 > 44 || intI2 < 0) {
                            views.delete(cc[0]);
                        } else {
                            views.set(cc[0], new ViewsObject(cc[0], id, i1, i2, i3));
                        }
                    }
                });
            }

            async function addView(input, parts) {
                try {
                    if (parts[2].includes(bot.id) && parts[3].includes(""95"")) {
                        const strArray3 = parts[4].split(';');
                        const num1 = parseInt(strArray3[2], 10);
                        const num2 = Math.abs(num1 - 9 - 300);
                        myPosition = num2;
                        console.log('log', { message: `Позиция нашего бота: [${num2}]` });
                    }

                    if (parts[2].includes(gameBotId) || parts[2].includes(bot.id)) {
                        if (isPaused) return;
                        await processAddView(input);
                    }
                } catch (e) {
                    console.log('log', { message: `Ошибка в addView: ${e.message}` });
                }
            }

            async function processAddView(input) {
                try {
                    const ls = input.split(/\s+/);

                    if (ls[2] === bot.id) {
                        if (ls.length > 0) {

                            const sw = ls[4].split("";"")[1];

                            switch (sw) {
                                case ""cn/s"":
                                    console.log('log', { message: ""Наша пушка: Обычная [Чёрная]"" });
                                    break;
                                case ""cn/gcn"":
                                    console.log('log', { message: ""Наша пушка: Улучшённая [Золотая]"" });
                                    break;
                                default:
                                    const positionMap = {
                                        ""cn/c1"": 1, ""cn/c2"": 2, ""cn/c3"": 3, ""cn/c4"": 4, ""cn/c5"": 5,
                                        ""cn/gcn1"": 1, ""cn/gcn2"": 2, ""cn/gcn3"": 3, ""cn/gcn4"": 4, ""cn/gcn5"": 5
                                    };
                                    if (positionMap[sw]) {
                                        myWeapons = positionMap[sw];
                                        console.log('log', { message: `Позиция пушки: [${myWeapons}]` });
                                    }
                                    break;
                            }
                        }
                    }

                    if (/light_r/i.test(input)) {
                        light = false;
                        console.log('log', { message: ""Загорелся красный свет!"" });
                    } else if (/light_g/i.test(input)) {
                        light = true;
                        console.log('log', { message: ""Загорелся зелёный свет!"" });

                        if (action && light) {
                            if (greenLamp) {
                                socket.send(`ACTION 6770 ${gameBotId}\r\n`);
                                console.log('log', { message: ""Стреляем с зелёным светом!"" });
                            }
                            else {
                                const randomDelay = randomNext(2000, 5000);
                                const seconds = (randomDelay / 1000).toFixed(1);
                                console.log('log', { message: `Выстрелим через [${seconds}] секунд!` });

                                await delay(randomDelay);

                                if (action && light) {
                                    socket.send(`ACTION 2401 ${gameBotId}\r\n`);
                                } else {
                                    console.log('log', { message: ""Отменяем команду Огонь, выстрелили зелёным светом!"" });
                                }
                            }
                        } else {
                            console.log('log', { message: ""Отменяем команду Огонь!"" });
                        }
                    }

                } catch (e) {
                    console.log('log', { message: `Ошибка в processAddView: ${e.message}` });
                }
            }

            async function searchObj() {
                try {
                    action = false;

                    for (const view of views.values()) {
                        if (view.id === ""6000"") {
                            const i1 = parseInt(view.i1, 10);
                            const i2 = parseInt(view.i2, 10);
                            const i3 = parseInt(view.i3, 10);

                            if (!isNaN(i1) && !isNaN(i2) && !isNaN(i3)) {
                                if (i1 === -50 && i2 === 0 && i3 === 33) {
                                    const randomPosition = randomNext(363, 368);
                                    prizePosition = 8;
                                    nextPosition = randomPosition;
                                    nextWeapons = 3;
                                    action = true;
                                    break;
                                } else if (i1 === -25 && i2 === 0 && i3 === 33) {
                                    const randomPosition = randomNext(378, 388);
                                    prizePosition = 9;
                                    nextPosition = randomPosition;
                                    nextWeapons = 3;
                                    action = true;
                                    break;
                                } else if (i1 === 0 && i2 === 0 && i3 === 33) {
                                    const randomPosition = randomNext(390, 399);
                                    prizePosition = 10;
                                    nextPosition = randomPosition;
                                    nextWeapons = 3;
                                    action = true;
                                    break;
                                }

                                if (action) break;
                            }
                        }
                    }
                } catch (error) {
                    console.log('log', { message: `Ошибка в searchObj: ${error.message}` });
                }

                return Promise.resolve();
            }

            async function weaponControl() {
                if (myWeapons !== nextWeapons) {
                    if (myWeapons === nextWeapons) {
                        console.log('log', { message: ""Позиция пушки уже на нужной позиции. Ход пропускается."" });
                        return;
                    }

                    if (nextWeapons < 1 || nextWeapons > 5) {
                        console.log('log', { message: ""Заданная позиция пушки выходит за пределы допустимых значений (1-5). Ход пропускается."" });
                        return;
                    }

                    const randomDelay = randomNext(4000, 5000);
                    const seconds = (randomDelay / 1000).toFixed(1);
                    console.log('log', { message: `Сменим позицию пушки через [${seconds}] секунд!` });

                    await delay(randomDelay);

                    if (myWeapons < nextWeapons) {
                        weaponsUp();
                    } else {
                        weaponsDown();
                    }
                }
            }

            async function controlPosition() {
                const isWithinRange = isWithinPrizeRange(prizePosition, myPosition);
                const randomDelay = randomNext(1500, 3000);

                if (!isWithinRange && myPosition !== nextPosition) {
                    myPosition = nextPosition;

                    const seconds = (randomDelay / 1000).toFixed(1);
                    console.log('log', { message: `Сменим позицию бота через [${seconds}] секунд!` });

                    await delay(randomDelay);

                    socket.send(`REMOVE ${nextPosition}\r\n`);
                }
            }

            function remove(parts) {
                if (parts[1].includes(bot.id)) {
                    myPosition = parseInt(parts[2]);
                    console.log('log', { message: `Текущая позиция нашего бота: ${parts[2]}` });
                }
            }

            function isWithinPrizeRange(prizePosition, myPosition) {
                switch (prizePosition) {
                    case 8:
                        return myPosition >= 363 && myPosition <= 368;
                    case 9:
                        return myPosition >= 378 && myPosition <= 388;
                    case 10:
                        return myPosition >= 390 && myPosition <= 399;
                    default:
                        return false;
                }
            }

            function weaponsDown() {
                const num = myWeapons - nextWeapons;
                for (let index = 0; index < num; index++) {
                    socket.send(`ACTION 2403 ${gameBotId}\r\n`);
                }

                myWeapons = nextWeapons;
                console.log('log', { message: `Опустили пушку на [${nextWeapons}] позицию!` });
            }

            function weaponsUp() {
                const num = nextWeapons - myWeapons;
                for (let index = 0; index < num; index++) {
                    socket.send(`ACTION 2402 ${gameBotId}\r\n`);
                }

                myWeapons = nextWeapons;
                console.log('log', { message: `Подняли пушку на [${nextWeapons}] позицию!` });
            }

            function loadBall() {
                if (!ballValue) {
                    console.log(""log"", { message: ""Режим ядра не выбран! Нажмите 'f' или 'e' для выбора."" });
                    return;
                }

                const trimmedValue = ballValue.trim();
                switch (trimmedValue) {
                    case ""explosive"":
                        console.log(""log"", { message: ""Заряжаем разрывное ядро!"" });
                        socket.send(`ACTION 8910 ${gameBotId}\r\n`);
                        break;
                    case ""fire"":
                        console.log(""log"", { message: ""Заряжаем огненное ядро!"" });
                        socket.send(`ACTION 4493 ${gameBotId}\r\n`);
                        break;
                    default:
                        console.log(""log"", { message: `Неизвестный режим ядра: ${trimmedValue}` });
                        break;
                }
            }

            function delay(ms) {
                return new Promise((resolve) => setTimeout(resolve, ms));
            }

            function randomNext(min, max) {
                return Math.floor(Math.random() * (max - min + 1)) + min;
            }

            function parser353(str, join = false) {
                try {
                    const CHARACTER_PARAMS_PER_SUIT = 5;
                    const tokens = str.trim().split(/\s+/);

                    let i = 0;
                    while (i < tokens.length) {
                        let clan = tokens[i];
                        let nick = tokens[i + 1];
                        const id = tokens[i + 2];
                        const K = Math.abs(parseInt(tokens[i + 3], 10));
                        const position = tokens[i + 4 + K * CHARACTER_PARAMS_PER_SUIT];

                        let stars = false;
                        let owner = false;

                        if (nick.startsWith('+')) {
                            nick = nick.substring(1).trim();
                            stars = true;
                        }

                        if (nick.startsWith('@')) {
                            nick = nick.substring(1).trim();
                            owner = true;
                        }

                        i += 5 + K * CHARACTER_PARAMS_PER_SUIT;

                        const userData = {
                            id: parseInt(id, 10),
                            nick,
                            clan,
                            position: parseInt(position, 10),
                            owner,
                            stars,
                            join
                        };

                        users.set(userData.id, new User(userData));

                        console.log('data', userData);

                        if (join) break;
                    }
                } catch (e) {
                    console.error(`parser 353 error: ${e.message}`);
                }
            }

            function handle900(parts) {
                bot.currentPlanet = parts[1];
                console.log('log', { message: `Успешно авторизовались: ${bot.nick} [${parts[1]}]` });

                if (parts[1].toLowerCase().includes(""cannon*"")) {
                    gameRun = true;

                    const nickname = parts[1].trim();
                    const user = searchNickUser(nickname);

                    if (user) {
                        gameBotId = user.id;
                        console.log('log', { message: `Найден пользователь: ${user.nick}, ID: ${user.id}` });
                    } else {
                        console.log('log', { message: `Пользователь с ником ""${nickname}"" не найден.` });
                    }
                }
                else {
                    gameRun = false;
                }
            }

            function userJoin(input, nick, id) {
                if (bot.id !== id.toString()) {
                    console.log('log', { message: `На планету залетел персонаж => ${nick}` });
                }

                const userJoin = getUser(id);
                if (!userJoin) {
                    const joinData = input.trim().substring(input.indexOf("" "") + 1);
                    parser353(joinData, true);
                }
            }

            function searchNickUser(nick) {
                for (const user of users.values()) {
                    if (user.nick.toLowerCase().includes(nick.toLowerCase())) {
                        return user;
                    }
                }
                return null;
            }

            function getUser(id) {
                return users.get(id) || null;
            }

            function removeUser(id) {
                users.delete(id);
            }

            function userExit(id) {
                const exitingUser = getUser(id);
                if (exitingUser) {
                    console.log('log', { message: `Покинул планету => ${exitingUser.nick}` });
                }
                removeUser(id);
            }

            function getTextAfterSecondColon(input) {
                const firstColonIndex = input.indexOf("":"");
                if (firstColonIndex === -1) return """";

                const secondColonIndex = input.indexOf("":"", firstColonIndex + 1);
                if (secondColonIndex === -1) return """";

                return input.substring(secondColonIndex + 1).trim();
            }

            function getTextAfterSecondColonArray(input) {
                const firstColonIndex = input.indexOf("":"");
                if (firstColonIndex === -1) return [];

                const secondColonIndex = input.indexOf("":"", firstColonIndex + 1);
                if (secondColonIndex === -1) return [];

                const textAfterSecondColon = input.substring(secondColonIndex + 1).trim();

                const separators = /[ , \n\r]+/;
                const result = textAfterSecondColon.split(separators).filter(Boolean);

                return result;
            }

            function closeDialogs() {
                // Закрытие диалогов с интервалом в 1 секунду
                for (let i = 1; i <= 5; i++) {
                    setTimeout(() => {
                        const closeButton = document.querySelector("".dialog__close-button"");
                        if (closeButton) closeButton.click();
                    }, i * 1000);
                }
            }

            return socket;
        };
    } catch (error) {
        console.log(""ws_error"", { error: error.message });
    }
})();";

            // 🔹 Настройки минификатора
            var settings = new CodeSettings
            {
                EvalTreatment = EvalTreatment.MakeAllSafe, // безопасная обработка eval
                PreserveImportantComments = false,         // удаляем все комментарии
                LocalRenaming = LocalRenaming.CrunchAll,      // переименование локальных переменных
                OutputMode = OutputMode.SingleLine,        // вывод в одну строку
                TermSemicolons = true,                     // завершаем ; если нужно
            };

            // 🔹 Выполняем минификацию / лёгкую обфускацию
            var result = Uglify.Js(jsCode, settings);

            string outputJs;
            if (result.HasErrors)
            {
                // Если NUglify что-то не смог обработать, возвращаем оригинал
                foreach (var error in result.Errors)
                    _logger.LogError("NUglify JS error: {Error}", error.ToString());

                outputJs = jsCode;
            }
            else
            {
                outputJs = result.Code;
                _logger.LogInformation("✅ JS script minified successfully. Original size: {Orig} bytes, Minified: {Min} bytes",
                    jsCode.Length, outputJs.Length);
            }

            // 🔹 Добавляем заголовки, запрещаем кеш
            Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            Response.Headers.Append("Pragma", "no-cache");
            Response.Headers.Append("Expires", "0");
            Response.Headers.Append("Content-Type", "application/javascript; charset=utf-8");

            return Content(outputJs, "application/javascript; charset=utf-8");
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