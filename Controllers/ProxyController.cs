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

        public ProxyController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet]
        public async Task<IActionResult> GetProxiedPage()
        {
            const string targetUrl = "https://galaxy.mobstudio.ru/web/";
            var client = _httpClientFactory.CreateClient();

            try
            {
                var response = await client.GetAsync(targetUrl);
                response.EnsureSuccessStatusCode();
                var htmlContent = await response.Content.ReadAsStringAsync();

                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(htmlContent);

                // 1. ������� <head>
                var headNode = htmlDoc.DocumentNode.SelectSingleNode("//head");
                if (headNode != null)
                {
                    // 2. ������� ��� <base>
                    var baseNode = htmlDoc.CreateElement("base");
                    baseNode.SetAttributeValue("href", targetUrl);

                    // 3. ��������� <base> � ������ <head>
                    headNode.PrependChild(baseNode);
                }

                // 4. ������� � �������� ��� ������
                var scriptNode = htmlDoc.CreateElement("script");
                scriptNode.InnerHtml = @"
// ��� ��������� JavaScript ���
console.log('������ ������� �������!');
alert('������ �� ����������� �������!');
// ����� ����� ���� ����� ���� ������
";

                htmlDoc.DocumentNode.SelectSingleNode("//body").AppendChild(scriptNode);

                return Content(htmlDoc.DocumentNode.OuterHtml, "text/html");
            }
            catch (HttpRequestException e)
            {
                return StatusCode(502, $"�� ������� ��������� ��������: {e.Message}");
            }
        }
    }
}