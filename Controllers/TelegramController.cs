using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TelegramController : ControllerBase
    {
        /// <summary>
        /// Отправляет сообщение в Telegram
        /// </summary>
        /// <param name="request">Запрос с сообщением</param>
        /// <returns>Результат отправки</returns>
        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromBody] SendTelegramMessageRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { error = "Сообщение не может быть пустым" });
            }

            try
            {
                bool success = await TelegramService.SendMessageAsync(request.Message);

                if (success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Сообщение успешно отправлено в Telegram"
                    });
                }
                else
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        error = "Не удалось отправить сообщение в Telegram"
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = $"Внутренняя ошибка сервера: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Отправляет форматированное сообщение в Telegram
        /// </summary>
        /// <param name="request">Запрос с данными для форматирования</param>
        /// <returns>Результат отправки</returns>
        [HttpPost("send-formatted-message")]
        public async Task<IActionResult> SendFormattedMessage([FromBody] SendFormattedTelegramMessageRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { error = "Заголовок сообщения не может быть пустым" });
            }

            try
            {
                var formattedMessage = FormatMessage(request);
                bool success = await TelegramService.SendMessageAsync(formattedMessage);

                if (success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Форматированное сообщение успешно отправлено в Telegram"
                    });
                }
                else
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        error = "Не удалось отправить сообщение в Telegram"
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = $"Внутренняя ошибка сервера: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Проверяет статус Telegram бота
        /// </summary>
        /// <returns>Статус бота</returns>
        [HttpGet("status")]
        public async Task<IActionResult> GetBotStatus()
        {
            try
            {
                // Отправляем тестовое сообщение для проверки статуса
                bool success = await TelegramService.SendMessageAsync("🤖 Проверка статуса бота");

                return Ok(new
                {
                    success = success,
                    status = success ? "Бот работает корректно" : "Бот недоступен",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = $"Ошибка при проверке статуса: {ex.Message}"
                });
            }
        }

        private string FormatMessage(SendFormattedTelegramMessageRequest request)
        {
            var message = $"*{request.Title}*\n\n";

            if (!string.IsNullOrWhiteSpace(request.Description))
            {
                message += $"{request.Description}\n\n";
            }

            if (request.Data != null && request.Data.Any())
            {
                message += "📊 *Данные:*\n";
                foreach (var item in request.Data)
                {
                    message += $"• {item.Key}: `{item.Value}`\n";
                }
                message += "\n";
            }

            if (!string.IsNullOrWhiteSpace(request.Footer))
            {
                message += $"_{request.Footer}_";
            }

            return message;
        }
    }

    public class SendTelegramMessageRequest
    {
        public string Message { get; set; }
    }

    public class SendFormattedTelegramMessageRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public Dictionary<string, string> Data { get; set; }
        public string Footer { get; set; }
    }
}
