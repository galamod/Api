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
        Task<ConcurrentDictionary<int, Galaxy.user>> GetUsersAsync(string connectionId);
        void CleanupExpiredConnections();
    }

    public class ConnectionManager : IConnectionManager
    {
        private readonly ConcurrentDictionary<string, SecureConnection> _connections;
        private readonly ILogger<ConnectionManager> _logger;
        private readonly Timer _cleanupTimer;

        public ConnectionManager(ILogger<ConnectionManager> logger)
        {
            _connections = new ConcurrentDictionary<string, SecureConnection>();
            _logger = logger;

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
                // Создаем экземпляр Galaxy и подключаемся
                connection.GalaxyClient = new Galaxy();

                // Запускаем подключение в отдельной задаче
                var connectTask = Task.Run(async () =>
                {
                    await Galaxy.Connect(password, planetName);
                });

                // Ждем некоторое время для установки соединения
                await Task.Delay(2000);

                // Проверяем, установлено ли соединение
                if (Galaxy.IsConnectionActive)
                {
                    connection.IsConnected = true;
                    connection.BotId = Galaxy.Bot.Instance.id;
                    connection.BotNick = Galaxy.Bot.Instance.nick;
                    connection.BotPass = Galaxy.Bot.Instance.pass;

                    _connections.TryAdd(connection.ConnectionId, connection);

                    _logger.LogInformation($"Создано новое соединение Galaxy: {connection.ConnectionId} для планеты {planetName}");

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

        public Task<bool> SendMessageAsync(string connectionId, string message)
        {
            if (_connections.TryGetValue(connectionId, out var connection) && connection.IsActive())
            {
                try
                {
                    Galaxy.Send(message);
                    connection.UpdateActivity();
                    return Task.FromResult(true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при отправке сообщения через соединение {connectionId}: {ex.Message}");
                    return Task.FromResult(false);
                }
            }

            return Task.FromResult(false);
        }

        public Task<ConcurrentDictionary<int, Galaxy.user>> GetUsersAsync(string connectionId)
        {
            if (_connections.TryGetValue(connectionId, out var connection) && connection.IsActive())
            {
                connection.UpdateActivity();
                return Task.FromResult(Galaxy.users);
            }

            return Task.FromResult(new ConcurrentDictionary<int, Galaxy.user>());
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
