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
            var targetUrl = new Uri(new Uri(TargetBaseUrl), path);

            try
            {
                var response = await client.GetAsync(targetUrl);

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
                }

                var contentType = response.Content.Headers.ContentType?.ToString();

                // ���� ��� HTML-��������, ������������ ���
                if (contentType != null && contentType.Contains("text/html"))
                {
                    var html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    // 1. ������� ������� ����������� Service Worker
                    var scriptNodes = doc.DocumentNode.SelectNodes("//script");
                    if (scriptNodes != null)
                    {
                        foreach (var script in scriptNodes.ToList())
                        {
                            if (script.InnerHtml.Contains("serviceWorker.register"))
                            {
                                script.Remove();
                            }
                        }
                    }

                    // 2. �������� <base> ���
                    var head = doc.DocumentNode.SelectSingleNode("//head");
                    if (head != null)
                    {
                        var baseTag = doc.CreateElement("base");
                        baseTag.SetAttributeValue("href", TargetBaseUrl);
                        head.PrependChild(baseTag);
                    }

                    // 3. �������� ��� ��������� ������
                    var body = doc.DocumentNode.SelectSingleNode("//body");
                    if (body != null)
                    {
                        var scriptNode = doc.CreateElement("script");
                        scriptNode.InnerHtml = "alert('������ �� ����������� �������!'); console.log('������ ������� �������.');";
                        body.AppendChild(scriptNode);
                    }

                    var modifiedHtml = doc.DocumentNode.OuterHtml;
                    return Content(modifiedHtml, "text/html", Encoding.UTF8);
                }
                else
                {
                    // ��� ���� ��������� ����� �������� (CSS, JS, ��������) ������ ���������� ��
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