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
        private readonly Timer _cleanupTimer;

        public ConnectionManager(ILogger<ConnectionManager> logger)
        {
            _connections = new ConcurrentDictionary<string, SecureConnection>();
            _logger = logger;

            // Устанавливаем логгер для Galaxy
            Galaxy.SetLogger(logger as ILogger<Galaxy>);

            // Подписываемся на события Galaxy
            Galaxy.ConnectionStateChanged += OnGalaxyConnectionStateChanged;
            Galaxy.LogMessage += OnGalaxyLogMessage;

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

                // Создаем экземпляр Galaxy
                connection.GalaxyClient = new Galaxy();

                // Запускаем подключение
                var connectResult = await Galaxy.Connect(password, planetName);

                await Task.Delay(TimeSpan.FromSeconds(1));

                if (connectResult && Galaxy.IsConnectionActive)
                {
                    connection.IsConnected = true;
                    connection.BotId = Galaxy.Bot.Instance.id;
                    connection.BotNick = Galaxy.Bot.Instance.nick;
                    connection.BotPass = Galaxy.Bot.Instance.pass;

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

        private void OnGalaxyConnectionStateChanged(string botNick, bool isConnected)
        {
            _logger.LogInformation($"Состояние подключения Galaxy изменено - Бот: {botNick}, Подключен: {isConnected}");
        }

        private void OnGalaxyLogMessage(string message)
        {
            _logger.LogInformation($"Galaxy: {message}");
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

            // Отписываемся от событий
            Galaxy.ConnectionStateChanged -= OnGalaxyConnectionStateChanged;
            Galaxy.LogMessage -= OnGalaxyLogMessage;

            foreach (var connection in _connections.Values)
            {
                connection.Dispose();
            }

            _connections.Clear();
        }
    }
}
