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
       // private readonly string _model = "google/gemini-2.0-flash-exp:free"; // Model OpenRouter
        private readonly Dictionary<long, List<ChatMessage>> _userConversations = new(); // Contexts for each user

        public OpenAIChat(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        public async Task<string> GetResponse(long chatId, string userMessage)
        {
            try
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

                // Logging the conversation state
                Console.WriteLine($"Current conversation for chatId {chatId}:");
                foreach (var msg in _userConversations[chatId])
                {
                    Console.WriteLine($"- {msg.Role}: {msg.Content.Substring(0, Math.Min(50, msg.Content.Length))}...");
                }

                // Prepare the request body according to the OpenRouter API format
                var requestBody = new
                {
                    model = _model,
                    messages = _userConversations[chatId].Select(m => new { role = m.Role, content = m.Content }).ToList()
                };

                // Serialize and log the request body
                var json = JsonSerializer.Serialize(requestBody);
                Console.WriteLine($"Request to OpenRouter API: {json}");

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Send POST request to OpenRouter endpoint
                Console.WriteLine("Sending request to OpenRouter API...");
                var response = await _httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);

                // Read the response content
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"OpenRouter API response status: {response.StatusCode}");
                Console.WriteLine($"OpenRouter API response content: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"OpenRouter API error. Status: {response.StatusCode}. Content: {responseContent}");
                    return $"Sorry, I encountered an error. API Status: {response.StatusCode}";
                }
                // Try to parse the response as JSON
                try
                {
                    using var doc = JsonDocument.Parse(responseContent);

                    // Let's examine the structure of the response
                    Console.WriteLine("JSON Response structure:");
                    LogJsonElement(doc.RootElement);

                    // Try to find the message content
                    string aiMessage = ExtractMessageContent(doc.RootElement);

                    if (string.IsNullOrEmpty(aiMessage))
                    {
                        Console.WriteLine("Failed to extract AI message from response");
                        return "Sorry, I couldn't extract a proper response from the API.";
                    }

                    Console.WriteLine($"Extracted AI message: {aiMessage}");

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
                catch (JsonException jsonEx)
                {
                    Console.WriteLine($"JSON parsing error: {jsonEx.Message}");
                    return "Sorry, I encountered an error parsing the API response.";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in GetResponse: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return "Sorry, I encountered an unexpected error processing your request.";
            }
        }

        private string ExtractMessageContent(JsonElement rootElement)
        {
            // Try different possible paths to find the message content

            // Standard OpenAI API format
            if (rootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];

                // Try the standard structure
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString();
                }

                // Alternative structure sometimes used
                if (firstChoice.TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }
            }

            // Some APIs use a different format
            if (rootElement.TryGetProperty("response", out var response))
            {
                return response.GetString();
            }

            if (rootElement.TryGetProperty("output", out var output))
            {
                return output.GetString();
            }

            // If we can't find any of the expected structures, return null
            return null;
        }

        private void LogJsonElement(JsonElement element, string prefix = "")
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    Console.WriteLine($"{prefix}Object {{");
                    foreach (var property in element.EnumerateObject())
                    {
                        Console.WriteLine($"{prefix}  {property.Name}:");
                        LogJsonElement(property.Value, prefix + "    ");
                    }
                    Console.WriteLine($"{prefix}}}");
                    break;

        case JsonValueKind.Array:
                    Console.WriteLine($"{prefix}Array [");
                    int index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        Console.WriteLine($"{prefix}  [{index}]:");
                        LogJsonElement(item, prefix + "    ");
                        index++;
                    }
                    Console.WriteLine($"{prefix}]");
                    break;

                case JsonValueKind.String:
                    Console.WriteLine($"{prefix}String: {element.GetString()?.Substring(0, Math.Min(50, element.GetString()?.Length ?? 0))}...");
                    break;

                default:
                    Console.WriteLine($"{prefix}{element.ValueKind}: {element}");
                    break;
            }
        }
    }
}
