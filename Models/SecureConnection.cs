using Api.Services;

namespace Api.Models
{
    public class SecureConnection : IDisposable
    {
        public string ConnectionId { get; set; }
        public Galaxy GalaxyClient { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsConnected { get; set; }
        public string Password { get; set; }
        public string PlanetName { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }

        // Информация о подключенном боте
        public string BotId { get; set; }
        public string BotNick { get; set; }
        public string BotPass { get; set; }

        public SecureConnection()
        {
            ConnectionId = Guid.NewGuid().ToString();
            CreatedAt = DateTime.UtcNow;
            LastActivity = DateTime.UtcNow;
            CancellationTokenSource = new CancellationTokenSource();
        }

        public void UpdateActivity()
        {
            LastActivity = DateTime.UtcNow;
        }

        public bool IsActive()
        {
            return GalaxyClient != null && Galaxy.IsConnectionActive && IsConnected;
        }

        public void Dispose()
        {
            try
            {
                CancellationTokenSource?.Cancel();
                Galaxy.Close();
                CancellationTokenSource?.Dispose();
            }
            catch (Exception ex)
            {
                // Логирование ошибок при необходимости
                Console.WriteLine($"Ошибка при закрытии соединения {ConnectionId}: {ex.Message}");
            }

            IsConnected = false;
        }
    }
}
