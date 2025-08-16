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
        public static TcpClient client { get; private set; }
        public static SslStream sslStream { get; private set; }
        public static NetworkStream networkStream { get; private set; }

        private readonly Random random = new Random();
        private static CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly StaticWebUserAgentGenerator staticWebUserAgent = new StaticWebUserAgentGenerator();

        // Добавляем логгер
        private static ILogger<Galaxy> _logger;

        // Событие для уведомления о состоянии подключения
        public static event Action<string, bool> ConnectionStateChanged;
        public static event Action<string> LogMessage;

        public static void SetLogger(ILogger<Galaxy> logger)
        {
            _logger = logger;
        }

        public static async Task<bool> Connect(string password, string planetName)
        {
            Galaxy client = new Galaxy();
            return await client.TcpClientConnectAsync(password, planetName);
        }

        public async Task<bool> TcpClientConnectAsync(string password, string planetName)
        {
            string userAgent = staticWebUserAgent.GeneratedUserAgent;
            string phoneUserAgent = staticWebUserAgent.GetPhoneUserAgent(userAgent);

            _cts = new CancellationTokenSource();
            string hash = null;
            bool connectionSuccessful = false;

            try
            {
                LogInfo($"Начинаем подключение к Galaxy серверу для планеты: {planetName}");

                using (client = new TcpClient(hostname: "cs.mobstudio.ru", port: 6671))
                using (networkStream = client.GetStream())
                using (sslStream = new SslStream(innerStream: networkStream, leaveInnerStreamOpen: false,
                    userCertificateValidationCallback: new RemoteCertificateValidationCallback(ValidateServerCertificate),
                    userCertificateSelectionCallback: SelectClientCertificate))
                {
                    await sslStream.AuthenticateAsClientAsync(targetHost: "cs.mobstudio.ru", clientCertificates: null,
                        enabledSslProtocols: SslProtocols.Tls12, checkCertificateRevocation: false);

                    var cert = sslStream.RemoteCertificate;
                    if (cert == null)
                    {
                        LogError("Сертификат сервера не получен");
                        return false;
                    }

                    var x509 = new X509Certificate2(cert);
                    var expireDate = x509.NotAfter;
                    var daysLeft = (expireDate - DateTime.UtcNow).Days;

                    LogInfo($"Сертификат истекает: {expireDate} (через {daysLeft} дней)");

                    if (!_cts.Token.IsCancellationRequested && IsConnectionActive)
                    {
                        Send($":ru IDENT 355 -1 0000 1 2 :GALA");
                        LogInfo("Отправлена команда IDENT");
                    }

                    do
                    {
                        var (input, parts, msg, message, str) = await ReadFromStream(_cts.Token);

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
                                Bot.Instance.id = parts[1];
                                Bot.Instance.pass = parts[2];
                                Bot.Instance.nick = parts[3];
                                LogInfo($"Регистрация бота: {Bot.Instance.nick}");
                                break;
                            case "403":
                                if (input.Contains("no such channel", StringComparison.CurrentCulture))
                                {
                                    await Task.Delay(random.Next(2500, 3500));
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
                                    await Task.Delay(random.Next(2500, 3500));
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
                                LogInfo($"Успешно авторизовались: [{Bot.Instance.nick}] [{parts[1].ToUpper().Trim()}]");
                                connectionSuccessful = true;
                                ConnectionStateChanged?.Invoke(Bot.Instance.nick, true);
                                Close();
                                CleanUpConnection();
                                return true;
                            case "999":
                                var randomAddons = random.Next(251700, 251799);
                                Send($"FWLISTVER {random.Next(310, 320)}");
                                Send($"ADDONS {randomAddons} 1");
                                Send($"MYADDONS {randomAddons} 1");
                                Send(phoneUserAgent);
                                Send($"JOIN {planetName.Trim()}");
                                LogInfo($"Подключение к планете: {planetName}");
                                break;
                        }

                    } while (!_cts.Token.IsCancellationRequested && IsConnectionActive);
                }
            }
            catch (Exception ex)
            {
                LogError($"Ошибка подключения: {ex.Message}");

                // Попытка переподключения только если это не критическая ошибка
                if (!connectionSuccessful && !_cts.Token.IsCancellationRequested)
                {
                    LogInfo("Попытка переподключения через 5 секунд...");
                    await Task.Delay(5000);
                    return await TcpClientConnectAsync(password, planetName);
                }
            }
            finally
            {
                CleanUpConnection();
            }

            return connectionSuccessful;
        }

        public static async void Send(string message)
        {
            if (sslStream == null || !sslStream.CanWrite) return;

            byte[] data = Encoding.UTF8.GetBytes($"{message}\r\n");
            await sslStream.WriteAsync(data, 0, data.Length, _cts.Token);
            await sslStream.FlushAsync(_cts.Token); // ✅
        }

        public static void Close()
        {
            try
            {
                Console.WriteLine("Закрытие соединения...");

                // Попытаться отправить QUIT
                try
                {
                    if (IsConnectionActive)
                        Send("QUIT :ds");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при отправке QUIT: {ex.Message}");
                }

                _cts.Cancel(); // останавливаем цикл чтения

                // Закрытие ssl-потока
                if (sslStream != null)
                {
                    try { sslStream.Close(); } catch { }
                    try { sslStream.Dispose(); } catch { }
                    sslStream = null;
                }

                // Закрытие клиента
                if (client != null)
                {
                    try { client.Close(); } catch { }
                    try { client.Dispose(); } catch { }
                    client = null;
                }

                Console.WriteLine("Соединение закрыто.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при закрытии соединения: {ex}");
            }
        }

        private static void CleanUpConnection()
        {
            // Останавливаем работу SslStream
            // Закрытие клиента
            if (client != null)
            {
                try { client.Close(); } catch { }
                try { client.Dispose(); } catch { }
                client = null;
            }

            // Закрытие ssl-потока
            if (sslStream != null)
            {
                try { sslStream.Close(); } catch { }
                try { sslStream.Dispose(); } catch { }
                sslStream = null;
            }
        }

        private static void LogInfo(string message)
        {
            _logger?.LogInformation(message);
            LogMessage?.Invoke($"[INFO] {message}");
            Console.WriteLine($"[INFO] {message}");
        }

        private static void LogWarning(string message)
        {
            _logger?.LogWarning(message);
            LogMessage?.Invoke($"[WARNING] {message}");
            Console.WriteLine($"[WARNING] {message}");
        }

        private static void LogError(string message)
        {
            _logger?.LogError(message);
            LogMessage?.Invoke($"[ERROR] {message}");
            Console.WriteLine($"[ERROR] {message}");
        }

        private async Task<(string, string[], string, string, string)> ReadFromStream(CancellationToken token)
        {
            byte[] buffer = new byte[2048 * 4];

            int bytesRead = 0;
            try
            {
                bytesRead = await sslStream.ReadAsync(buffer, 0, buffer.Length, token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("⏱️ Чтение отменено через CancellationToken.");
                return ("", Array.Empty<string>(), "", "", "");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка чтения: {ex.Message}");
            }

            if (bytesRead == 0)
            {
                Console.WriteLine("⚠️ Сервер закрыл соединение или не прислал данных.");
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

        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return sslPolicyErrors == SslPolicyErrors.None || sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch || sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors;
        }

        private static X509Certificate SelectClientCertificate(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate remoteCertificate, string[] acceptableIssuers)
        {
            return null;
        }

        public static bool IsConnectionActive
        {
            get
            {
                // Проверка соединения TcpClient
                if (client != null && client.Connected)
                {
                    if (client.Client?.LocalEndPoint is IPEndPoint localEndPoint && client.Client.RemoteEndPoint is IPEndPoint remoteEndPoint)
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

                // Если ни одна из проверок не прошла
                return false;
            }
        }

        public class Bot
        {
            private static Bot instance;
            public string nick { get; set; }
            public string pass { get; set; }
            public string id { get; set; }

            private Bot() { }

            public static Bot Instance
            {
                get
                {
                    if (instance == null)
                    {
                        instance = new Bot();
                    }
                    return instance;
                }
            }
        }
    }
}