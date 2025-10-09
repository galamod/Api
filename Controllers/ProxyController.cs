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

                // 1. Находим <head>
                var headNode = htmlDoc.DocumentNode.SelectSingleNode("//head");
                if (headNode != null)
                {
                    // 2. Создаем тег <base>
                    var baseNode = htmlDoc.CreateElement("base");
                    baseNode.SetAttributeValue("href", targetUrl);

                    // 3. Добавляем <base> в начало <head>
                    headNode.PrependChild(baseNode);
                }

                // 4. Создаем и внедряем ваш скрипт
                var scriptNode = htmlDoc.CreateElement("script");
                scriptNode.InnerHtml = @"
// Ваш кастомный JavaScript код
console.log('Скрипт успешно внедрен!');
alert('Привет от внедренного скрипта!');
// Здесь может быть любая ваша логика
";

                htmlDoc.DocumentNode.SelectSingleNode("//body").AppendChild(scriptNode);

                return Content(htmlDoc.DocumentNode.OuterHtml, "text/html");
            }
            catch (HttpRequestException e)
            {
                return StatusCode(502, $"Не удалось загрузить страницу: {e.Message}");
            }
        }
    }
}