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

        [HttpGet("connect")]
        public async Task<IActionResult> ConnectToServerGet([FromQuery] string password, [FromQuery] string planetName = "default")
        {
            return await ConnectToServer(password, planetName);
        }

        [HttpPost("connect/{password}")]
        public async Task<IActionResult> ConnectToServer(string password, [FromQuery] string planetName = "default")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(password))
                {
                    _logger.LogWarning("Попытка подключения с пустым паролем");
                    return BadRequest(new { message = "Пароль не может быть пустым" });
                }

                if (string.IsNullOrWhiteSpace(planetName))
                {
                    planetName = "default";
                }

                _logger.LogInformation($"Запрос на подключение к Galaxy серверу, планета: {planetName}");

                var connectionId = await _connectionManager.CreateConnectionAsync(password, planetName);

                _logger.LogInformation($"Успешное подключение к Galaxy серверу, ID соединения: {connectionId}");

                return Ok(new
                {
                    success = true,
                    message = "Успешное подключение к Galaxy серверу",
                    connectionId = connectionId,
                    planetName = planetName,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при подключении к Galaxy серверу");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Ошибка подключения к Galaxy серверу",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("status/{connectionId}")]
        public async Task<IActionResult> GetConnectionStatus(string connectionId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(connectionId))
                {
                    return BadRequest(new { message = "ID соединения не может быть пустым" });
                }

                var status = await _connectionManager.GetConnectionStatusAsync(connectionId);
                var connection = await _connectionManager.GetConnectionAsync(connectionId);

                if (connection != null)
                {
                    _logger.LogInformation($"Запрос статуса соединения {connectionId}: {status}");

                    return Ok(new
                    {
                        success = true,
                        connectionId,
                        status,
                        planetName = connection.PlanetName,
                        botNick = connection.BotNick,
                        botId = connection.BotId,
                        connectedAt = connection.CreatedAt,
                        lastActivity = connection.LastActivity,
                        isActive = connection.IsActive(),
                        timestamp = DateTime.UtcNow
                    });
                }

                _logger.LogWarning($"Соединение {connectionId} не найдено");
                return NotFound(new
                {
                    success = false,
                    message = "Соединение не найдено",
                    connectionId,
                    status = "Unknown",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при проверке статуса соединения {connectionId}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Ошибка при проверке статуса",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpDelete("disconnect/{connectionId}")]
        public async Task<IActionResult> DisconnectFromServer(string connectionId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(connectionId))
                {
                    return BadRequest(new { message = "ID соединения не может быть пустым" });
                }

                _logger.LogInformation($"Запрос на отключение соединения: {connectionId}");

                var result = await _connectionManager.CloseConnectionAsync(connectionId);

                if (result)
                {
                    _logger.LogInformation($"Galaxy соединение {connectionId} успешно закрыто");
                    return Ok(new
                    {
                        success = true,
                        message = "Galaxy соединение успешно закрыто",
                        connectionId,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    _logger.LogWarning($"Соединение {connectionId} не найдено или уже закрыто");
                    return BadRequest(new
                    {
                        success = false,
                        message = "Соединение не найдено или уже закрыто",
                        connectionId,
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при закрытии Galaxy соединения {connectionId}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Ошибка при закрытии соединения",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpPost("send/{connectionId}")]
        public async Task<IActionResult> SendMessage(string connectionId, [FromBody] SendMessageRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(connectionId))
                {
                    return BadRequest(new { message = "ID соединения не может быть пустым" });
                }

                if (string.IsNullOrWhiteSpace(request?.Message))
                {
                    return BadRequest(new { message = "Сообщение не может быть пустым" });
                }

                _logger.LogInformation($"Отправка сообщения через соединение {connectionId}: {request.Message}");

                var result = await _connectionManager.SendMessageAsync(connectionId, request.Message);

                if (result)
                {
                    _logger.LogInformation($"Сообщение успешно отправлено через соединение {connectionId}");
                    return Ok(new
                    {
                        success = true,
                        message = "Сообщение отправлено",
                        connectionId,
                        sentMessage = request.Message,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    _logger.LogWarning($"Ошибка отправки сообщения через соединение {connectionId}");
                    return BadRequest(new
                    {
                        success = false,
                        message = "Ошибка отправки сообщения",
                        connectionId,
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при отправке сообщения через соединение {connectionId}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Ошибка при отправке сообщения",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("connections")]
        public async Task<IActionResult> GetAllConnections()
        {
            try
            {
                _logger.LogInformation("Запрос списка всех активных соединений");

                // Этот метод нужно добавить в IConnectionManager
                var connections = await _connectionManager.GetAllConnectionsAsync();

                return Ok(new
                {
                    success = true,
                    connectionCount = connections.Count,
                    connections = connections.Select(c => new
                    {
                        connectionId = c.ConnectionId,
                        planetName = c.PlanetName,
                        botNick = c.BotNick,
                        botId = c.BotId,
                        isConnected = c.IsConnected,
                        isActive = c.IsActive(),
                        createdAt = c.CreatedAt,
                        lastActivity = c.LastActivity
                    }),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка соединений");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Ошибка при получении списка соединений",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpPost("cleanup")]
        public IActionResult CleanupExpiredConnections()
        {
            try
            {
                _logger.LogInformation("Запуск очистки неактивных соединений");

                _connectionManager.CleanupExpiredConnections();

                return Ok(new
                {
                    success = true,
                    message = "Очистка неактивных соединений выполнена",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при очистке соединений");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Ошибка при очистке соединений",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }
    }

    public class SendMessageRequest
    {
        public string Message { get; set; }
    }
}
