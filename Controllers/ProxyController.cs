using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Api.Controllers
{
    //[ApiController]
    //[Route("api/[controller]")]
    //public class ProxyController : ControllerBase
    //{
    //    private readonly IHttpClientFactory _httpClientFactory;
    //    private readonly ILogger<ProxyController> _logger;
    //    private const string TargetBaseUrl = "https://galaxy.mobstudio.ru/web/";

    //    public ProxyController(IHttpClientFactory httpClientFactory, ILogger<ProxyController> logger)
    //    {
    //        _httpClientFactory = httpClientFactory;
    //        _logger = logger;
    //    }

    //    // --- Глобальные CORS ---
    //    private void AddCorsHeaders(HttpResponse response)
    //    {
    //        response.Headers["Access-Control-Allow-Origin"] = "*";
    //        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
    //        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
    //    }

    //    [HttpOptions("{*path}")]
    //    public IActionResult Options()
    //    {
    //        AddCorsHeaders(Response);
    //        return Ok();
    //    }

    //    private void AddGalaxyHeaders(HttpRequestMessage request)
    //    {
    //        request.Headers.Add("x-galaxy-client-ver", "9.5");
    //        request.Headers.Add("x-galaxy-kbv", "352");
    //        request.Headers.Add("x-galaxy-lng", "ru");
    //        request.Headers.Add("x-galaxy-model", "chrome 140.0.0.0");
    //        request.Headers.Add("x-galaxy-platform", "web");
    //        request.Headers.Add("x-galaxy-user-agent", "Mozilla/5.0");
    //    }

    //    [Route("{*path}")]
    //    public async Task<IActionResult> Proxy(string path = "")
    //    {
    //        AddCorsHeaders(Response);

    //        var client = _httpClientFactory.CreateClient();
    //        var targetUrl = string.IsNullOrEmpty(path)
    //            ? new Uri(TargetBaseUrl)
    //            : new Uri(new Uri(TargetBaseUrl), path);

    //        try
    //        {
    //            var request = new HttpRequestMessage(new HttpMethod(Request.Method), targetUrl);
    //            AddGalaxyHeaders(request);

    //            // копируем тело запроса, если есть
    //            if (Request.ContentLength > 0)
    //            {
    //                using var reader = new StreamReader(Request.Body);
    //                var body = await reader.ReadToEndAsync();
    //                request.Content = new StringContent(body, Encoding.UTF8, Request.ContentType ?? "application/x-www-form-urlencoded");
    //            }

    //            // отправляем
    //            var response = await client.SendAsync(request);
    //            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

    //            var bytes = await response.Content.ReadAsByteArrayAsync();

    //            // --- 1️⃣ Если это JS-файл ---
    //            if (contentType.Contains("javascript") || path.EndsWith(".js"))
    //            {
    //                var js = Encoding.UTF8.GetString(bytes);

    //                // Заменяем все ссылки на Galaxy → на наш прокси
    //                js = js.Replace("https://galaxy.mobstudio.ru/", "/api/proxy/");

    //                // Можно внедрить свой код для отладки
    //                js = "alert('✅ JS переписан через прокси');\n" + js;

    //                return Content(js, "application/javascript; charset=utf-8", Encoding.UTF8);
    //            }

    //            // --- 2️⃣ Если это HTML ---
    //            if (contentType.Contains("text/html"))
    //            {
    //                var html = Encoding.UTF8.GetString(bytes);

    //                // Переписываем ссылки внутри JS, HTML и форм
    //                html = html.Replace("https://galaxy.mobstudio.ru/", "/api/proxy/");

    //                var doc = new HtmlDocument();
    //                doc.LoadHtml(html);

    //                AddBaseTag(doc);
    //                RewriteUrls(doc);
    //                InjectCustomScript(doc);

    //                var modifiedHtml = doc.DocumentNode.OuterHtml;
    //                return Content(modifiedHtml, "text/html; charset=utf-8", Encoding.UTF8);
    //            }

    //            // --- 3️⃣ Всё остальное просто отдаём ---
    //            return File(bytes, contentType);
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex, "Ошибка при проксировании {Url}", targetUrl);
    //            return StatusCode(500, "Ошибка проксирования: " + ex.Message);
    //        }
    //    }

    //    // === Вспомогательные методы ===

    //    private void AddBaseTag(HtmlDocument doc)
    //    {
    //        var head = doc.DocumentNode.SelectSingleNode("//head") ?? doc.DocumentNode.AppendChild(doc.CreateElement("head"));
    //        var oldBase = head.SelectSingleNode("//base");
    //        oldBase?.Remove();

    //        var baseTag = doc.CreateElement("base");
    //        baseTag.SetAttributeValue("href", "/api/proxy/");
    //        head.PrependChild(baseTag);
    //    }

    //    private void RewriteUrls(HtmlDocument doc)
    //    {
    //        var nodes = doc.DocumentNode.SelectNodes("//*[@src or @href or @action]");
    //        if (nodes == null) return;

    //        foreach (var node in nodes)
    //        {
    //            foreach (var attr in new[] { "src", "href", "action" })
    //            {
    //                var value = node.GetAttributeValue(attr, null);
    //                if (string.IsNullOrEmpty(value)) continue;
    //                if (value.StartsWith("#") || value.StartsWith("data:")) continue;

    //                if (value.StartsWith("https://galaxy.mobstudio.ru/"))
    //                    value = value.Replace("https://galaxy.mobstudio.ru/", "/api/proxy/");

    //                else if (!value.StartsWith("http://") && !value.StartsWith("https://"))
    //                    value = "/api/proxy/" + value.TrimStart('/');

    //                node.SetAttributeValue(attr, value);
    //            }
    //        }
    //    }

    //    private void InjectCustomScript(HtmlDocument doc)
    //    {
    //        var jsCode = "alert('✅ Внедрён кастомный JS из прокси');";
    //        var scriptNode = HtmlNode.CreateNode($"<script>{jsCode}</script>");
    //        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode.AppendChild(doc.CreateElement("body"));
    //        body.AppendChild(scriptNode);
    //    }
    //}
}