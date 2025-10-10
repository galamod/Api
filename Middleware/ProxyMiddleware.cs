using HtmlAgilityPack;
using System.Text;

namespace Api.Middleware
{
    public class ProxyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ProxyMiddleware> _logger;

        private const string TargetBaseUrl = "https://galaxy.mobstudio.ru/web/";

        public ProxyMiddleware(RequestDelegate next, IHttpClientFactory httpClientFactory, ILogger<ProxyMiddleware> logger)
        {
            _next = next;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Path.StartsWithSegments("/api/proxy"))
            {
                await _next(context);
                return;
            }

            var relativePath = context.Request.Path.Value!.Replace("/api/proxy", "").TrimStart('/');
            var targetUrl = new Uri(new Uri(TargetBaseUrl), relativePath + context.Request.QueryString);

            _logger.LogInformation("🔁 Проксируем запрос: {Url}", targetUrl);

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);

            // Копируем тело, если есть
            if (context.Request.ContentLength > 0)
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                request.Content = new StringContent(body, Encoding.UTF8, context.Request.ContentType ?? "application/x-www-form-urlencoded");
            }

            // Добавляем кастомные заголовки
            AddGalaxyHeaders(request);

            // Отправляем
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

            // Добавляем CORS
            AddCorsHeaders(context.Response);

            var bytes = await response.Content.ReadAsByteArrayAsync();

            // === Обработка JS ===
            if (contentType.Contains("javascript") || relativePath.EndsWith(".js"))
            {
                var js = Encoding.UTF8.GetString(bytes);

                // Заменяем обращения к исходному сайту и корню /web/
                js = js.Replace("https://galaxy.mobstudio.ru/", "/api/proxy/");
                js = js.Replace("\"/web/", "\"/api/proxy/web/");
                js = js.Replace(@"'\/web/", "'/api/proxy/web/");

                js = "alert('✅ JS через middleware модифицирован');\n" + js;

                context.Response.ContentType = "application/javascript; charset=utf-8";
                await context.Response.WriteAsync(js, Encoding.UTF8);
                return;
            }


            // === Обработка HTML ===
            if (contentType.Contains("text/html"))
            {
                var html = Encoding.UTF8.GetString(bytes);
                html = html.Replace("https://galaxy.mobstudio.ru/", "/api/proxy/");

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                RewriteUrls(doc);
                AddBaseTag(doc);
                InjectCustomScript(doc);

                var modifiedHtml = doc.DocumentNode.OuterHtml;

                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(modifiedHtml, Encoding.UTF8);
                return;
            }

            // === Всё остальное ===
            context.Response.ContentType = contentType;
            await context.Response.Body.WriteAsync(bytes);
        }

        private void AddCorsHeaders(HttpResponse response)
        {
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
        }

        private void AddGalaxyHeaders(HttpRequestMessage request)
        {
            request.Headers.Add("x-galaxy-client-ver", "9.5");
            request.Headers.Add("x-galaxy-platform", "web");
            request.Headers.Add("x-galaxy-model", "chrome");
        }

        private void AddBaseTag(HtmlDocument doc)
        {
            var head = doc.DocumentNode.SelectSingleNode("//head") ?? doc.DocumentNode.AppendChild(doc.CreateElement("head"));
            var oldBase = head.SelectSingleNode("//base");
            oldBase?.Remove();

            var baseTag = doc.CreateElement("base");
            baseTag.SetAttributeValue("href", "/api/proxy/");
            head.PrependChild(baseTag);
        }

        private void RewriteUrls(HtmlDocument doc)
        {
            var nodes = doc.DocumentNode.SelectNodes("//*[@src or @href or @action]");
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                foreach (var attr in new[] { "src", "href", "action" })
                {
                    var value = node.GetAttributeValue(attr, null);
                    if (string.IsNullOrEmpty(value)) continue;
                    if (value.StartsWith("#") || value.StartsWith("data:") || value.StartsWith("mailto:")) continue;

                    // ✅ Если это абсолютный путь на galaxy.mobstudio.ru
                    if (value.StartsWith("https://galaxy.mobstudio.ru/"))
                    {
                        value = value.Replace("https://galaxy.mobstudio.ru/", "/api/proxy/");
                    }
                    // ✅ Если начинается с /web/
                    else if (value.StartsWith("/web/"))
                    {
                        value = "/api/proxy" + value;
                    }
                    // ✅ Если относительный путь
                    else if (!value.StartsWith("http"))
                    {
                        value = "/api/proxy/" + value.TrimStart('/');
                    }

                    node.SetAttributeValue(attr, value);
                }
            }
        }


        private void InjectCustomScript(HtmlDocument doc)
        {
            var jsCode = "alert('✅ Middleware вставил JS');";
            var scriptNode = HtmlNode.CreateNode($"<script>{jsCode}</script>");
            var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode.AppendChild(doc.CreateElement("body"));
            body.AppendChild(scriptNode);
        }
    }

    // Расширение для регистрации
    public static class ProxyMiddlewareExtensions
    {
        public static IApplicationBuilder UseGalaxyProxy(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ProxyMiddleware>();
        }
    }
}
