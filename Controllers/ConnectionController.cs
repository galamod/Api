using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConnectionController : ControllerBase
    {
        private readonly IConnectionManager _connectionManager;
        private readonly ILogger<ConnectionController> _logger;

        public ConnectionController(IConnectionManager connectionManager, ILogger<ConnectionController> logger)
        {
            _connectionManager = connectionManager;
            _logger = logger;
        }

        [HttpPost("connect/{password}")]
        public async Task<IActionResult> ConnectToServer(string password, [FromQuery] string planetName = "default")
        {
            try
            {
                if (string.IsNullOrEmpty(password))
                {
                    return BadRequest("Пароль не может быть пустым");
                }

                var connectionId = await _connectionManager.CreateConnectionAsync(password, planetName);

                return Ok(new
                {
                    message = "Успешное подключение к Galaxy серверу",
                    connectionId = connectionId,
                    planetName = planetName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при подключении к Galaxy серверу");
                return StatusCode(500, new
                {
                    message = "Ошибка подключения к Galaxy серверу",
                    error = ex.Message
                });
            }
        }

        [HttpGet("status/{connectionId}")]
        public async Task<IActionResult> GetConnectionStatus(string connectionId)
        {
            try
            {
                var status = await _connectionManager.GetConnectionStatusAsync(connectionId);
                var connection = await _connectionManager.GetConnectionAsync(connectionId);

                if (connection != null)
                {
                    return Ok(new
                    {
                        connectionId,
                        status,
                        planetName = connection.PlanetName,
                        botNick = connection.BotNick,
                        botId = connection.BotId,
                        connectedAt = connection.CreatedAt,
                        lastActivity = connection.LastActivity
                    });
                }

                return Ok(new { connectionId, status });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке статуса соединения");
                return StatusCode(500, new
                {
                    message = "Ошибка при проверке статуса",
                    error = ex.Message
                });
            }
        }

        [HttpDelete("disconnect/{connectionId}")]
        public async Task<IActionResult> DisconnectFromServer(string connectionId)
        {
            try
            {
                var result = await _connectionManager.CloseConnectionAsync(connectionId);

                if (result)
                {
                    return Ok(new { message = "Galaxy соединение успешно закрыто" });
                }
                else
                {
                    return BadRequest(new { message = "Соединение не найдено или уже закрыто" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при закрытии Galaxy соединения");
                return StatusCode(500, new
                {
                    message = "Ошибка при закрытии соединения",
                    error = ex.Message
                });
            }
        }

        [HttpPost("send/{connectionId}")]
        public async Task<IActionResult> SendMessage(string connectionId, [FromBody] SendMessageRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Message))
                {
                    return BadRequest("Сообщение не может быть пустым");
                }

                var result = await _connectionManager.SendMessageAsync(connectionId, request.Message);

                if (result)
                {
                    return Ok(new { message = "Сообщение отправлено" });
                }
                else
                {
                    return BadRequest(new { message = "Ошибка отправки сообщения" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке сообщения");
                return StatusCode(500, new
                {
                    message = "Ошибка при отправке сообщения",
                    error = ex.Message
                });
            }
        }

        [HttpGet("users/{connectionId}")]
        public async Task<IActionResult> GetUsers(string connectionId)
        {
            try
            {
                var users = await _connectionManager.GetUsersAsync(connectionId);

                return Ok(new
                {
                    connectionId,
                    userCount = users.Count,
                    users = users.Values.Select(u => new
                    {
                        id = u.id,
                        nick = u.nick,
                        clan = u.clan,
                        position = u.position,
                        author = u.author,
                        stars = u.stars,
                        owner = u.owner,
                        join = u.join
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка пользователей");
                return StatusCode(500, new
                {
                    message = "Ошибка при получении списка пользователей",
                    error = ex.Message
                });
            }
        }
    }

    public class SendMessageRequest
    {
        public string Message { get; set; }
    }
}
