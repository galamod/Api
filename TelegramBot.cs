using System.Text;
using System.Text.Json;

namespace Api
{
    public class TelegramBot
    {
        public static async Task SendMessageAsync(string text)
        {
            const int maxLinesPerMessage = 80;

            if (string.IsNullOrEmpty(text))
                return;

            var lines = text.Split('\n');
            var messages = new List<string>();

            for (int i = 0; i < lines.Length; i += maxLinesPerMessage)
            {
                int linesToTake = Math.Min(maxLinesPerMessage, lines.Length - i);
                var messageLines = lines.Skip(i).Take(linesToTake);
                messages.Add(string.Join("\n", messageLines));
            }

            using (HttpClient httpClient = new HttpClient())
            {
                foreach (var message in messages)
                {
                    var payload = new
                    {
                        chat_id = 996096296,
                        text = message,
                        parse_mode = "Markdown"
                    };

                    string json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await httpClient.PostAsync(
                        "https://api.telegram.org/bot7512391054:AAFI6zZBgcc2WY67jOyOQmf5xg8ekztDj_k/sendMessage",
                        content
                    );

                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        //Console.WriteLine($"Message sent successfully. Response: {responseBody}");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to send the message. HTTP Status: {response.StatusCode}");
                    }

                    // Небольшая пауза, чтобы избежать ограничения Telegram API
                    await Task.Delay(500);
                }
            }
        }
    }
}
