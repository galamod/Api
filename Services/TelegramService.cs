using System.Text;
using System.Text.Json;

namespace Api.Services
{
    public class TelegramService
    {
        private const string BotToken = "7512391054:AAFI6zZBgcc2WY67jOyOQmf5xg8ekztDj_k";
        private const long ChatId = 996096296;
        private const int MaxLinesPerMessage = 80;

        public static async Task<bool> SendMessageAsync(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            try
            {
                var messages = SplitMessage(message);

                using (HttpClient httpClient = new HttpClient())
                {
                    foreach (var msg in messages)
                    {
                        var success = await SendSingleMessageAsync(httpClient, msg);
                        if (!success)
                            return false;

                        // Пауза между сообщениями для избежания лимитов API
                        await Task.Delay(500);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке сообщения в Telegram: {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> SendSingleMessageAsync(HttpClient httpClient, string message)
        {
            var payload = new
            {
                chat_id = ChatId,
                text = message,
                parse_mode = "Markdown"
            };

            string json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await httpClient.PostAsync(
                $"https://api.telegram.org/bot{BotToken}/sendMessage",
                content
            );

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Сообщение успешно отправлено. Ответ: {responseBody}");
                return true;
            }
            else
            {
                Console.WriteLine($"Не удалось отправить сообщение. HTTP Status: {response.StatusCode}");
                return false;
            }
        }

        private static List<string> SplitMessage(string text)
        {
            var lines = text.Split('\n');
            var messages = new List<string>();

            for (int i = 0; i < lines.Length; i += MaxLinesPerMessage)
            {
                int linesToTake = Math.Min(MaxLinesPerMessage, lines.Length - i);
                var messageLines = lines.Skip(i).Take(linesToTake);
                messages.Add(string.Join("\n", messageLines));
            }

            return messages;
        }
    }
}
