using Microsoft.AspNetCore.Mvc;

namespace Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class PingController : ControllerBase
    {
        /// <summary>
        /// Проверка, что сервер работает
        /// </summary>
        [HttpGet]
        public IActionResult Get()
        {
            return Ok("Server is alive");
        }
    }
}
