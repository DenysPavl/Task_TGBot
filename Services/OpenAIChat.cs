using System.Text;
using System.Text.Json;

namespace Telegram_Task_Bot.Services
{
    public class OpenAIChat
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model = "deepseek/deepseek-chat-v3-0324:free"; // Модель OpenRouter

        public OpenAIChat(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        public async Task<string> GetResponse(string userMessage)
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = "Ти корисний, ввічливий Telegram-бот, який допомагає з автострахуванням в компаніїї `Car Insurance`. Ціна страхування якої лише $100(не говорити на початку розмови). Якщо користувач бажає оформити поліс запропонуй використати команду /insurance" },
                    new { role = "user", content = userMessage }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Error: {response.StatusCode}\n{error}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return message;

        }
    }
}

