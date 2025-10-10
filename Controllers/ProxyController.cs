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
                    // --- ������ ��������� ---

                    // 1. ������ ����� ��� �����, � �� ������
                    var responseBytes = await response.Content.ReadAsByteArrayAsync();
                    var doc = new HtmlDocument();

                    // 2. ������������� ��������� �� ��������� ��� �������
                    doc.OptionDefaultStreamEncoding = Encoding.UTF8;
                    using (var stream = new MemoryStream(responseBytes))
                    {
                        // 3. ��������� HTML �� ������, �������� HAP ��������� ���������� ���������
                        doc.Load(stream, true);
                    }

                    // --- ����� ��������� ---

                    var scriptNodes = doc.DocumentNode.SelectNodes("//script");
                    if (scriptNodes != null)
                    {
                        foreach (var script in scriptNodes.ToList())
                        {
                            var src = script.GetAttributeValue("src", string.Empty);
                            if (script.InnerHtml.Contains("serviceWorker.register") || src.Contains("sw.js") || src.Contains("service-worker"))
                            {
                                script.Remove();
                                _logger.LogInformation("������ ������ Service Worker.");
                            }
                        }
                    }

                    var head = doc.DocumentNode.SelectSingleNode("//head");
                    if (head != null)
                    {
                        var baseTag = doc.CreateElement("base");
                        baseTag.SetAttributeValue("href", TargetBaseUrl);
                        head.PrependChild(baseTag);
                    }

                    var body = doc.DocumentNode.SelectSingleNode("//body");
                    if (body != null)
                    {
                        var mainScript = doc.DocumentNode.SelectSingleNode("//script[@src]");
                        var testScript = doc.CreateElement("script");
                        
                        // ������ ������� � ����������
                        testScript.InnerHtml = @"alert('������, ���!'); console.log('�������� ������ � ���������� ��������.');";

                        if (mainScript != null)
                        {
                            mainScript.ParentNode.InsertBefore(testScript, mainScript);
                        }
                        else
                        {
                            body.AppendChild(testScript);
                        }
                    }

                    // --- ��������� ������� ���������� ---
                    
                    // 4. ��������� ���������� HTML � ����� � ����� ��������� ��������� UTF-8
                    using (var memoryStream = new MemoryStream())
                    {
                        doc.Save(memoryStream, Encoding.UTF8);
                        memoryStream.Position = 0;
                        // ���������� ��������� ��� FileStreamResult, ����� �������� ���������� ��������������
                        return new FileStreamResult(memoryStream, "text/html; charset=utf-8");
                    }
                }
                else
                {
                    var content = await response.Content.ReadAsByteArrayAsync();
                    return new FileContentResult(content, contentType ?? "application/octet-stream");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "������ ��� ������������� ������� �� {Url}", targetUrl);
                return StatusCode(500, "���������� ������ ������� ��� ������������� �������.");
            }
        }
    }
}