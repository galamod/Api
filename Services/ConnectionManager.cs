using Api.Models;
using System.Collections.Concurrent;

namespace Api.Services
{
    public interface IConnectionManager
    {
        Task<string> CreateConnectionAsync(string password, string planetName = "default");
        Task<bool> CloseConnectionAsync(string connectionId);
        Task<SecureConnection> GetConnectionAsync(string connectionId);
        Task<string> GetConnectionStatusAsync(string connectionId);
        Task<bool> SendMessageAsync(string connectionId, string message);
        Task<List<SecureConnection>> GetAllConnectionsAsync(); // Новый метод
        void CleanupExpiredConnections();
    }

    public class ConnectionManager : IConnectionManager
    {
        private readonly ConcurrentDictionary<string, SecureConnection> _connections;
        private readonly ILogger<ConnectionManager> _logger;
        private readonly ILogger<Galaxy> _galaxyLogger;
        private readonly Timer _cleanupTimer;

        public ConnectionManager(ILogger<ConnectionManager> logger, ILogger<Galaxy> galaxyLogger)
        {
            _connections = new ConcurrentDictionary<string, SecureConnection>();
            _logger = logger;
            _galaxyLogger = galaxyLogger;

            // Таймер для очистки неактивных соединений каждые 5 минут
            _cleanupTimer = new Timer(CleanupCallback, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task<string> CreateConnectionAsync(string password, string planetName = "default")
        {
            var connection = new SecureConnection
            {
                Password = password,
                PlanetName = planetName
            };

            try
            {
                _logger.LogInformation($"Создание нового Galaxy соединения для планеты: {planetName}");

                // Создаем экземпляр Galaxy с логгером
                connection.GalaxyClient = new Galaxy(_galaxyLogger);

                // Подписываемся на события этого конкретного соединения
                connection.GalaxyClient.ConnectionStateChanged += (botNick, isConnected) =>
                {
                    _logger.LogInformation($"Состояние подключения Galaxy изменено - Бот: {botNick}, Подключен: {isConnected}");
                };

                connection.GalaxyClient.LogMessage += (message) =>
                {
                    _logger.LogInformation($"Galaxy [{connection.ConnectionId}]: {message}");
                };

                // Запускаем подключение
                var connectResult = await connection.GalaxyClient.ConnectAsync(password, planetName);

                await Task.Delay(TimeSpan.FromSeconds(1));

                if (connectResult && connection.GalaxyClient.IsConnectionActive)
                {
                    connection.IsConnected = true;
                    connection.BotId = connection.GalaxyClient.Bot.Id;
                    connection.BotNick = connection.GalaxyClient.Bot.Nick;
                    connection.BotPass = connection.GalaxyClient.Bot.Pass;

                    _connections.TryAdd(connection.ConnectionId, connection);

                    _logger.LogInformation($"Успешно создано Galaxy соединение: {connection.ConnectionId} для планеты {planetName}, бот: {connection.BotNick}");

                    return connection.ConnectionId;
                }
                else
                {
                    throw new Exception("Не удалось установить соединение с Galaxy сервером");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при создании Galaxy соединения: {ex.Message}");
                connection.Dispose();
                throw;
            }
        }

        public Task<bool> CloseConnectionAsync(string connectionId)
        {
            if (_connections.TryRemove(connectionId, out var connection))
            {
                connection.Dispose();
                _logger.LogInformation($"Galaxy соединение закрыто: {connectionId}");
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task<SecureConnection> GetConnectionAsync(string connectionId)
        {
            _connections.TryGetValue(connectionId, out var connection);
            connection?.UpdateActivity();
            return Task.FromResult<SecureConnection>(connection);
        }

        public Task<string> GetConnectionStatusAsync(string connectionId)
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                if (connection.IsActive())
                {
                    return Task.FromResult("Connected");
                }
                else
                {
                    return Task.FromResult("Disconnected");
                }
            }

            return Task.FromResult("Unknown");
        }

        public async Task<bool> SendMessageAsync(string connectionId, string message)
        {
            if (_connections.TryGetValue(connectionId, out var connection) && connection.IsActive())
            {
                try
                {
                    await connection.GalaxyClient.Send(message);
                    connection.UpdateActivity();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при отправке сообщения через соединение {connectionId}: {ex.Message}");
                    return false;
                }
            }

            return false;
        }

        public Task<List<SecureConnection>> GetAllConnectionsAsync()
        {
            try
            {
                var activeConnections = _connections.Values
                    .Where(c => c.IsActive())
                    .ToList();

                _logger.LogInformation($"Найдено {activeConnections.Count} активных соединений из {_connections.Count} общих");

                return Task.FromResult(activeConnections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка всех соединений");
                return Task.FromResult(new List<SecureConnection>());
            }
        }

        public void CleanupExpiredConnections()
        {
            var expiredConnections = _connections.Values
                .Where(c => DateTime.UtcNow - c.LastActivity > TimeSpan.FromMinutes(30))
                .ToList();

            foreach (var connection in expiredConnections)
            {
                if (_connections.TryRemove(connection.ConnectionId, out var removedConnection))
                {
                    removedConnection.Dispose();
                    _logger.LogInformation($"Удалено неактивное Galaxy соединение: {connection.ConnectionId}");
                }
            }
        }

        private void CleanupCallback(object state)
        {
            CleanupExpiredConnections();
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();

            foreach (var connection in _connections.Values)
            {
                connection.Dispose();
            }

            _connections.Clear();
        }
    }
}

