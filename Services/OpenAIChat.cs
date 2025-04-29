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
        private readonly string _model = "google/gemini-2.5-pro-exp-03-25"; // Model OpenRouter
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
                    Content = "You are a helpful, polite Telegram bot that helps with car insurance at the company Car Insurance. The price of insurance is only $100 (don't say it at the beginning of the conversation). If the user says they want insurance, respond naturally and include the tag [ACTION:START_INSURANCE]"
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

            try
            {
                // Send POST request to OpenRouter endpoint
                var response = await _httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);

                // Read the response content
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine("OpenAI response: " + responseContent);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"OpenAI API error. Status: {response.StatusCode}. Content: {responseContent}");
                    return "Sorry, I encountered an error processing your request.";
                }

                // Parse the JSON response
                using var doc = JsonDocument.Parse(responseContent);

                // Extract the AI message from the response
                string aiMessage = null;

                // Check if the expected properties exist in the JSON
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content_value))
                {
                    aiMessage = content_value.GetString();
                }

                if (string.IsNullOrEmpty(aiMessage))
                {
                    Console.WriteLine("Failed to extract AI message from response");
                    return "Sorry, I couldn't generate a proper response.";
                }

                Console.WriteLine("OpenAI aiMessage: " + aiMessage);

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
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in GetResponse: {ex.Message}");
                return "Sorry, I encountered an error processing your request.";
            }
        }
    }
}
