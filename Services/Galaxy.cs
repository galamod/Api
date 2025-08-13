using Api.Helper;
using System.Collections.Concurrent;
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

        public static ConcurrentDictionary<int, user> users = new ConcurrentDictionary<int, user>();
        private readonly Random random = new Random();
        private static CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly StaticWebUserAgentGenerator staticWebUserAgent = new StaticWebUserAgentGenerator();

        public static async Task Connect(string password, string planetName)
        {
            Galaxy client = new Galaxy();

            await client.TcpClientConnectAsync(password, planetName);
        }

        public async Task TcpClientConnectAsync(string password, string planetName)
        {
            string userAgent = staticWebUserAgent.GeneratedUserAgent;
            string phoneUserAgent = staticWebUserAgent.GetPhoneUserAgent(userAgent);

            _cts = new CancellationTokenSource();

            string hash = null;

            try
            {
                using (client = new TcpClient(hostname: "cs.mobstudio.ru", port: 6671))
                using (networkStream = client.GetStream())
                using (sslStream = new SslStream(innerStream: networkStream, leaveInnerStreamOpen: false, userCertificateValidationCallback: new RemoteCertificateValidationCallback(ValidateServerCertificate), userCertificateSelectionCallback: SelectClientCertificate))
                {
                    await sslStream.AuthenticateAsClientAsync(targetHost: "cs.mobstudio.ru", clientCertificates: null, enabledSslProtocols: SslProtocols.Tls12, checkCertificateRevocation: false);

                    var cert = sslStream.RemoteCertificate;
                    if (cert == null)
                    {
                        Console.WriteLine("❌ Сертификат сервера не получен.");
                        return;
                    }

                    var x509 = new X509Certificate2(cert);
                    var expireDate = x509.NotAfter;
                    var daysLeft = (expireDate - DateTime.UtcNow).Days;

                    Console.WriteLine($"✅ Сертификат истекает: {expireDate} (через {daysLeft} дней)");

                    if (!_cts.Token.IsCancellationRequested && IsConnectionActive)
                    {
                        Send($":ru IDENT 355 -1 0000 1 2 :GALA");
                    }

                    do
                    {
                        var (input, parts, msg, message, str) = await ReadFromStream(_cts.Token);

                        string parts0 = parts.ElementAtOrDefault(0, string.Empty);
                        string parts1 = parts.ElementAtOrDefault(1, string.Empty);

                        switch (parts0)
                        {
                            case "PING":
                                Send("PONG");
                                break;
                            case "HAAAPSI":
                                hash = HashGenerator.GenerateHash(parts[1]);
                                Send($"RECOVER {password.Trim()}");
                                break;
                            case "REGISTER":
                                string randomHash = new RandomHashGenerator(16).GenerateRandomHash();
                                Send($"USER {parts[1]} {parts[2]} {parts[3]} {hash} {randomHash}");
                                Bot.Instance.id = parts[1];
                                Bot.Instance.pass = parts[2];
                                Bot.Instance.nick = parts[3];
                                break;
                            case "SLEEP":
                            case "PART":
                                userExit(int.Parse(parts[1].Trim()));
                                break;
                            case "JOIN":
                                userJoin(input, parts[2], int.Parse(parts[3]));
                                break;
                            case "OP":
                                OP(parts[1]);
                                break;
                            case "T":
                                user t = getUser(int.Parse(parts[1].Trim()));
                                if (t != null)
                                    Console.WriteLine($"{t.nick} печатает...");
                                break;
                            case "353":
                                parser353(str);
                                break;
                            case "403":
                                if (input.Contains("no such channel", StringComparison.CurrentCulture))
                                {
                                    _ = Task.Delay(random.Next(2500, 3500));
                                    Send($"JOIN {planetName}");
                                }
                                break;
                            case "421":
                                Console.WriteLine(input);
                                break;
                            case "451":
                                if (input.Contains("Неверный код", StringComparison.CurrentCultureIgnoreCase))
                                {
                                    Console.WriteLine("Неверный код восстановления!");
                                    Close();
                                }
                                break;
                            case "452":
                                if (input.Contains("заблокирован", StringComparison.CurrentCultureIgnoreCase))
                                {
                                    Console.WriteLine(input);
                                    Close();
                                }
                                break;
                            case "471":
                                if (input.Contains("channel is full", StringComparison.CurrentCulture))
                                {
                                    _ = Task.Delay(random.Next(2500, 3500));
                                    Send($"JOIN {planetName}");
                                }
                                break;
                            case "482":
                                Console.WriteLine(input);
                                break;
                            case "855":
                                Console.WriteLine("Остановка текущих задач и очистка состояния...");
                                if (_cts != null)
                                {
                                    _cts.Cancel();
                                    _cts.Dispose();
                                }
                                _cts = new CancellationTokenSource();
                                Console.WriteLine("Создан новый CancellationTokenSource для будущих задач.");
                                users.Clear();
                                break;
                            case "860":
                                parser860(input);
                                break;
                            case "863":
                                break;
                            case "900":
                                Console.WriteLine($"Успешно авторизовались: [{Bot.Instance.nick}] [{parts[1].ToUpper().Trim()}]");
                                break;
                            case "999":
                                var randomAddons = random.Next(251700, 251799);
                                Send($"FWLISTVER {random.Next(310, 320)}");
                                Send($"ADDONS {randomAddons} 1");
                                Send($"MYADDONS {randomAddons} 1");
                                Send(phoneUserAgent);
                                Send($"JOIN {planetName.Trim()}");
                                break;
                        }

                        switch (parts1)
                        {
                            case "KICK":
                            case "BAN":
                            case "PRISON":
                                user user = getUser(int.Parse(parts[2].Trim()));
                                userExit(int.Parse(parts[2].Trim()));
                                break;
                        }

                    } while (!_cts.Token.IsCancellationRequested && IsConnectionActive);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Неизвестная ошибка: {ex.Message}");
                await TcpClientConnectAsync(password, planetName);
            }
            finally
            {
                CleanUpConnection();
            }
        }

        private void parser353(string str, bool join = false)
        {
            try
            {
                const int CHARACTER_PARAMS_PER_SUIT = 5;
                string[] tokens = Regex.Split(str.Trim(), @"\s+");
                string nick, clan, id, position;

                for (int i = 0; i < tokens.Length;)
                {
                    bool owner = false, stars = false;

                    clan = tokens[i];
                    nick = tokens[i + 1];
                    id = tokens[i + 2];
                    int K = Math.Abs(int.Parse(tokens[i + 3]));
                    position = tokens[i + 4 + K * CHARACTER_PARAMS_PER_SUIT];

                    if (nick.StartsWith("+"))
                    {
                        nick = nick.Substring(1).Trim();
                        stars = true;
                    }

                    if (nick.StartsWith("@"))
                    {
                        nick = nick.Substring(1).Trim();
                        owner = true;
                    }

                    i += 5 + K * CHARACTER_PARAMS_PER_SUIT;

                    users[int.Parse(id)] = new user { id = int.Parse(id), nick = nick, clan = clan, position = int.Parse(position), owner = owner, stars = stars, join = join };

                    if (join)
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"parser 353 error: {e.Message}");
            }
        }

        public void parser860(string str)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(str)) return;

                string[] d = Regex.Split(str.Trim(), @"\s+");
                for (int i = 0; i < d.Length; i += 3)
                {
                    if (i + 1 >= d.Length) continue;
                    if (!int.TryParse(d[i + 1], out int id)) continue;

                    if (users.TryGetValue(id, out user user))
                    {
                        if (int.TryParse(d[i + 2], out int authority))
                        {
                            user.author = authority;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"parser 860 error: {e.Message}");
            }
        }

        private void OP(string userID)
        {
            int id = int.Parse(userID);

            if (users.TryGetValue(id, out var user))
            {
                user.stars = true;
            }
        }

        private static user getUser(int id)
        {
            // Изменения здесь: поиск пользователя по id в словаре
            if (users.TryGetValue(id, out var user))
            {
                return user;
            }
            return null;
        }

        private void removeUsers(int id)
        {
            users.TryRemove(id, out _);
        }

        private void userJoin(string input, string nick, int id)
        {
            if (Bot.Instance.id != id.ToString())
                Console.WriteLine($"На планету залетел персонаж ➜ {nick}");
            user userJoin = getUser(id);
            if (userJoin == null)
            {
                string join = input.Substring(input.Trim().IndexOf(" ") + 1);
                parser353(join, true);
            }
        }

        private void userExit(int id)
        {
            user userExit = getUser(id);
            if (userExit != null)
                Console.WriteLine($"Покинул планету ➜ {userExit.nick}");
            removeUsers(id);
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

        public class user
        {
            public int id { get; set; }

            public string nick { get; set; }

            public string clan { get; set; }

            public int position { get; set; }

            public int author { get; set; }

            public bool stars { get; set; }

            public bool owner { get; set; }

            public bool join { get; set; }
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