using Api.Helper;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Api.Services
{
    public class Galaxy
    {
        private TcpClient _client;
        private SslStream _sslStream;
        private NetworkStream _networkStream;
        private CancellationTokenSource _cts;
        private readonly Random _random = new Random();
        private readonly StaticWebUserAgentGenerator _staticWebUserAgent = new StaticWebUserAgentGenerator();
        private readonly ILogger<Galaxy> _logger;

        // Информация о боте для этого соединения
        public BotInfo Bot { get; private set; }

        // События для этого конкретного соединения
        public event Action<string, bool> ConnectionStateChanged;
        public event Action<string> LogMessage;

        public Galaxy(ILogger<Galaxy> logger = null)
        {
            _logger = logger;
            _cts = new CancellationTokenSource();
            Bot = new BotInfo();
        }

        public static async Task<bool> Connect(string password, string planetName)
        {
            Galaxy client = new Galaxy();
            return await client.TcpClientConnectAsync(password, planetName);
        }

        public async Task<bool> ConnectAsync(string password, string planetName)
        {
            return await TcpClientConnectAsync(password, planetName);
        }

        private async Task<bool> TcpClientConnectAsync(string password, string planetName)
        {
            string userAgent = _staticWebUserAgent.GeneratedUserAgent;
            string phoneUserAgent = _staticWebUserAgent.GetPhoneUserAgent(userAgent);

            _cts = new CancellationTokenSource();
            string hash = null;
            bool connectionSuccessful = false;

            try
            {
                LogInfo($"Начинаем подключение к Galaxy серверу для планеты: {planetName}");

                _client = new TcpClient("cs.mobstudio.ru", 6671);
                _networkStream = _client.GetStream();
                _sslStream = new SslStream(_networkStream, false,
                    new RemoteCertificateValidationCallback(ValidateServerCertificate),
                    SelectClientCertificate);

                await _sslStream.AuthenticateAsClientAsync("cs.mobstudio.ru", null,
                    SslProtocols.Tls12, false);

                var cert = _sslStream.RemoteCertificate;
                if (cert == null)
                {
                    LogError("Сертификат сервера не получен");
                    return false;
                }

                var x509 = new X509Certificate2(cert);
                var expireDate = x509.NotAfter;
                var daysLeft = (expireDate - DateTime.UtcNow).Days;

                LogInfo($"Сертификат истекает: {expireDate} (через {daysLeft} дней)");

                if (!_cts.Token.IsCancellationRequested)
                {
                    Send($":ru IDENT 355 -1 0000 1 2 :GALA");
                    LogInfo("Отправлена команда IDENT");
                }

                do
                {
                    var (input, parts, msg, message, str) = await ReadFromStreamAsync(_cts.Token);

                    if (string.IsNullOrEmpty(input))
                        continue;

                    string parts0 = parts.ElementAtOrDefault(0, string.Empty);
                    string parts1 = parts.ElementAtOrDefault(1, string.Empty);

                    LogInfo($"Получена команда: {parts0}");

                    switch (parts0)
                    {
                        case "PING":
                            Send("PONG");
                            LogInfo("Отправлен PONG в ответ на PING");
                            break;
                        case "HAAAPSI":
                            hash = HashGenerator.GenerateHash(parts[1]);
                            Send($"RECOVER {password.Trim()}");
                            LogInfo("Отправлена команда RECOVER");
                            break;
                        case "REGISTER":
                            string randomHash = new RandomHashGenerator(16).GenerateRandomHash();
                            Send($"USER {parts[1]} {parts[2]} {parts[3]} {hash} {randomHash}");
                            Bot.Id = parts[1];
                            Bot.Pass = parts[2];
                            Bot.Nick = parts[3];
                            LogInfo($"Регистрация бота: {Bot.Nick}");
                            break;
                        case "403":
                            if (input.Contains("no such channel", StringComparison.CurrentCulture))
                            {
                                await Task.Delay(_random.Next(2500, 3500));
                                Send($"JOIN {planetName}");
                                LogInfo($"Повторная попытка подключения к планете: {planetName}");
                            }
                            break;
                        case "421":
                            LogWarning($"Неизвестная команда: {input}");
                            break;
                        case "451":
                            if (input.Contains("Неверный код", StringComparison.CurrentCultureIgnoreCase))
                            {
                                LogError("Неверный код восстановления!");
                                Close();
                                return false;
                            }
                            break;
                        case "452":
                            if (input.Contains("заблокирован", StringComparison.CurrentCultureIgnoreCase))
                            {
                                LogError($"Аккаунт заблокирован: {input}");
                                Close();
                                return false;
                            }
                            break;
                        case "471":
                            if (input.Contains("channel is full", StringComparison.CurrentCulture))
                            {
                                await Task.Delay(_random.Next(2500, 3500));
                                Send($"JOIN {planetName}");
                                LogInfo($"Планета переполнена, повторная попытка: {planetName}");
                            }
                            break;
                        case "482":
                            LogWarning($"Недостаточно прав: {input}");
                            break;
                        case "855":
                            LogInfo("Остановка текущих задач и очистка состояния...");
                            if (_cts != null)
                            {
                                _cts.Cancel();
                                _cts.Dispose();
                            }
                            _cts = new CancellationTokenSource();
                            LogInfo("Создан новый CancellationTokenSource для будущих задач");
                            break;
                        case "863":
                            break;
                        case "900":
                            LogInfo($"Успешно авторизовались: [{Bot.Nick}] [{parts[1].ToUpper().Trim()}]");
                            connectionSuccessful = true;
                            ConnectionStateChanged?.Invoke(Bot.Nick, true);
                            return true;
                        case "999":
                            var randomAddons = _random.Next(251700, 251799);
                            Send($"FWLISTVER {_random.Next(310, 320)}");
                            Send($"ADDONS {randomAddons} 1");
                            Send($"MYADDONS {randomAddons} 1");
                            Send(phoneUserAgent);
                            Send($"JOIN {planetName.Trim()}");
                            LogInfo($"Подключение к планете: {planetName}");
                            break;
                    }

                } while (!_cts.Token.IsCancellationRequested && IsConnectionActive);
            }
            catch (Exception ex)
            {
                LogError($"Ошибка подключения: {ex.Message}");

                if (!connectionSuccessful && !_cts.Token.IsCancellationRequested)
                {
                    LogInfo("Попытка переподключения через 5 секунд...");
                    await Task.Delay(5000);
                    return await TcpClientConnectAsync(password, planetName);
                }
            }
            finally
            {
                if (!connectionSuccessful)
                {
                    CleanUpConnection();
                }
            }

            return connectionSuccessful;
        }

        public async void Send(string message)
        {
            if (_sslStream == null || !_sslStream.CanWrite) return;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes($"{message}\r\n");
                await _sslStream.WriteAsync(data, 0, data.Length, _cts.Token);
                await _sslStream.FlushAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                LogError($"Ошибка отправки сообщения: {ex.Message}");
                throw;
            }
        }

        public void Close()
        {
            try
            {
                LogInfo("Закрытие соединения...");

                try
                {
                    if (IsConnectionActive)
                        Send("QUIT :ds");
                }
                catch (Exception ex)
                {
                    LogError($"Ошибка при отправке QUIT: {ex.Message}");
                }

                _cts?.Cancel();
                CleanUpConnection();
                LogInfo("Соединение закрыто.");
            }
            catch (Exception ex)
            {
                LogError($"Ошибка при закрытии соединения: {ex}");
            }
        }

        private void CleanUpConnection()
        {
            try
            {
                _sslStream?.Close();
                _sslStream?.Dispose();
                _sslStream = null;

                _networkStream?.Close();
                _networkStream?.Dispose();
                _networkStream = null;

                _client?.Close();
                _client?.Dispose();
                _client = null;
            }
            catch (Exception ex)
            {
                LogError($"Ошибка при очистке соединения: {ex.Message}");
            }
        }

        private void LogInfo(string message)
        {
            _logger?.LogInformation(message);
            LogMessage?.Invoke($"[INFO] {message}");
            Console.WriteLine($"[INFO] {message}");
        }

        private void LogWarning(string message)
        {
            _logger?.LogWarning(message);
            LogMessage?.Invoke($"[WARNING] {message}");
            Console.WriteLine($"[WARNING] {message}");
        }

        private void LogError(string message)
        {
            _logger?.LogError(message);
            LogMessage?.Invoke($"[ERROR] {message}");
            Console.WriteLine($"[ERROR] {message}");
        }

        private async Task<(string, string[], string, string, string)> ReadFromStreamAsync(CancellationToken token)
        {
            byte[] buffer = new byte[2048 * 4];

            int bytesRead = 0;
            try
            {
                bytesRead = await _sslStream.ReadAsync(buffer, 0, buffer.Length, token);
            }
            catch (OperationCanceledException)
            {
                LogInfo("⏱️ Чтение отменено через CancellationToken.");
                return ("", Array.Empty<string>(), "", "", "");
            }
            catch (Exception ex)
            {
                LogError($"Ошибка чтения: {ex.Message}");
                return ("", Array.Empty<string>(), "", "", "");
            }

            if (bytesRead == 0)
            {
                LogWarning("⚠️ Сервер закрыл соединение или не прислал данных.");
                return ("", Array.Empty<string>(), "", "", "");
            }

            string input = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            string[] parts = Regex.Split(input, @"\s+");

            string msg = input.Contains(':') ? input.Split(':').Last().Trim() : string.Empty;

            int commaIndex = input.LastIndexOf(',');
            int colonIndex = input.LastIndexOf(':');

            string message =
                (commaIndex > colonIndex) ? input[(commaIndex + 1)..].Trim() :
                (colonIndex > commaIndex) ? input[(colonIndex + 1)..].Trim() :
                string.Empty;

            string info = input.Contains(':') ? input[(input.IndexOf(':') + 1)..].Trim() : string.Empty;

            return (input, parts, msg, message, info);
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return sslPolicyErrors == SslPolicyErrors.None ||
                   sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch ||
                   sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors;
        }

        private X509Certificate SelectClientCertificate(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate remoteCertificate, string[] acceptableIssuers)
        {
            return null;
        }

        public bool IsConnectionActive
        {
            get
            {
                if (_client != null && _client.Connected)
                {
                    if (_client.Client?.LocalEndPoint is IPEndPoint localEndPoint && _client.Client.RemoteEndPoint is IPEndPoint remoteEndPoint)
                    {
                        TcpConnectionInformation[] connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();

                        if (connections.Any(connection =>
                            connection.LocalEndPoint.Equals(localEndPoint) &&
                            connection.RemoteEndPoint.Equals(remoteEndPoint) &&
                            connection.State == TcpState.Established))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        public void Dispose()
        {
            Close();
            _cts?.Dispose();
        }

        public class BotInfo
        {
            public string Nick { get; set; }
            public string Pass { get; set; }
            public string Id { get; set; }
        }
    }
}