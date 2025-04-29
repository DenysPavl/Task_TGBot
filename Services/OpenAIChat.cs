using System.Text;
using System.Text.Json;

namespace Telegram_Task_Bot.Services
{
    public class ChatMessage
    {
        public string Role { get; set; } // "user", "assistant", "system"
        public string Content { get; set; }
    }

    public class OpenAIChat
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model = "deepseek/deepseek-chat-v3-0324:free"; // Model OpenRouter
        private readonly Dictionary<long, List<ChatMessage>> _userConversations = new(); // Contexts for each user

        public OpenAIChat(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        public async Task<string> GetResponse(long chatId, string userMessage)
        {
            //Initial goal for new AI chat
            if (!_userConversations.ContainsKey(chatId))
            {
                _userConversations[chatId] = new List<ChatMessage>
                {
                    new ChatMessage
                    {
                        Role = "system",
                        Content = "You are a helpful, polite Telegram bot that helps with car insurance at the company `Car Insurance`. The price of insurance is only $100 (don't say it at the beginning of the conversation). If the user says they want insurance, respond naturally and include the tag [ACTION:START_INSURANCE]. Do not mention the tag explicitly to the user."
                    }
                };
            }

            // Adding a new message from a user to the story
            _userConversations[chatId].Add(new ChatMessage
            {
                Role = "user",
                Content = userMessage
            });

            // Prepare the request body according to the OpenRouter API format
            var requestBody = new
            {
                model = _model,
                messages = _userConversations[chatId]
            };

            // Serialize the request body to JSON
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Send POST request to OpenRouter endpoint
            var response = await _httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Error: {response.StatusCode}\n{error}");
            }

            // Read and parse the JSON response
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine("OpenAI response: " + responseContent);
            using var doc = JsonDocument.Parse(responseContent);

            // Return the extracted generated message from JSON
            var aiMessage = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            // Adding the bot's response to the story
            _userConversations[chatId].Add(new ChatMessage
            {
                Role = "assistant",
                Content = aiMessage
            });

            // If the story is too long (> 30 messages), shorten it
            if (_userConversations[chatId].Count > 30)
            {
                // last 10 request-response pairs
                var systemPrompt = _userConversations[chatId].First();
                var lastMessages = _userConversations[chatId].Skip(Math.Max(1, _userConversations[chatId].Count - 20)).ToList();
                _userConversations[chatId] = new List<ChatMessage> { systemPrompt };
                _userConversations[chatId].AddRange(lastMessages);
            }

            return aiMessage;
        }
    }
}
