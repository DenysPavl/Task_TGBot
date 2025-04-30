using Telegram.Bot;
using Telegram.Bot.Types;
using Mindee;
using Mindee.Input;
using Mindee.Product.Passport;
using System.Collections.Concurrent;
using Telegram.Bot.Types.Enums;
using Mindee.Product.DriverLicense;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram_Task_Bot.Model;
using Telegram_Task_Bot.Services;
using System.Text.RegularExpressions;


namespace Telegram_Task_Bot
{
    class BotHost
    {
        TelegramBotClient _bot;
        private readonly OpenAIChat _openAI;
        private static readonly ConcurrentDictionary<long, string> _userState = new ConcurrentDictionary<long, string>();                      // User states (passport/license)
        private static readonly ConcurrentDictionary<long, string> _userLastMode = new();                                                      // User's last mode (passport/license)
        private static readonly ConcurrentDictionary<long, UserDocumentData> _userData = new ConcurrentDictionary <long, UserDocumentData>();  // User extracted document data

        public BotHost(string token, string openAI_key)
        {
            _bot = new TelegramBotClient(token);
            _openAI = new OpenAIChat(openAI_key);
        }

        // Set webhook for receiving updates
        public async Task SetWebhook(string webhookUrl)    
        {
            await _bot.SetWebhook(webhookUrl); 
            Console.WriteLine($"Webhook set to: {webhookUrl}");
        }

        // Main update handler for all types of updates
        public async Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken token)
        {
            Console.WriteLine("Received update");

            // Handle button presses (callback queries)
            if (update.CallbackQuery != null)
            {
                var chatId = update.CallbackQuery.Message.Chat.Id;
                var callbackData = update.CallbackQuery.Data;

                if (callbackData == "correct")
                {
                    await client.EditMessageReplyMarkup(chatId: chatId, messageId: update.CallbackQuery.Message.MessageId); // The keyboard disappears
                    await client.SendMessage(chatId, "The price of insurance is $100. Do you agree?", replyMarkup: InsuranceConsentKeyboard);
                }
                else if (callbackData == "resend")
                {
                    await client.EditMessageReplyMarkup(chatId: chatId, messageId: update.CallbackQuery.Message.MessageId); // The keyboard disappears
                    _userState[chatId] = "passport";
                    await client.SendMessage(chatId, "Please send your passport photo again.");
                }
                else if (callbackData == "agree")
                {
                    await client.EditMessageReplyMarkup(chatId: chatId, messageId: update.CallbackQuery.Message.MessageId); // The keyboard disappears

                    if (string.IsNullOrEmpty(_userData[chatId].PassportIdNumber))  // Check if data is complete
                    {
                        _userState[chatId] = "passport";
                        await client.SendMessage(chatId, "There was an error with your passport data! Please send a photo of your passport.");
                    }
                    else if (string.IsNullOrEmpty(_userData[chatId].DriversLicenseIdNumber)) // Check if data is complete
                    {
                        _userState[chatId] = "license";
                        await client.SendMessage(chatId, "There was an error with your driver's license data! Please send a photo of your driver's license.");
                    }
                    else
                        await client.SendMessage(chatId, $"📄 Your insurance policy:\n\n{GenerateInsurancePolicy(_userData[chatId].GivenName, _userData[chatId].Surname, _userData[chatId].PassportIdNumber, _userData[chatId].DriversLicenseIdNumber)}");

                }
                else if (callbackData == "disagree")
                {
                    await client.SendMessage(chatId, "Sorry, but $100 is the only available price.");
                }
                return;
            }

            if (update.Message == null)
                return;

            // Handle incoming text messages
            if (update.Message?.Text != null)
            {
                var chatId = update.Message.Chat.Id;
                var text = update.Message.Text.Trim();
                var reply = await _openAI.GetResponse(chatId, text); // get response from OpenAiChat service

                if (string.IsNullOrWhiteSpace(reply))
                {
                    await client.SendMessage(chatId, "Sorry, I couldn't understand. Please try again.");
                    return;
                }

                if (reply.Contains("[ACTION:START_INSURANCE]"))
                {
                    _userState[chatId] = "passport";
                    _userData[chatId] = new UserDocumentData(); // Reset previous data
                    var confirmPrompt = await _openAI.GetResponse(chatId, $"Notify the user that they are starting to apply for auto insurance. Ask them to send a passport photo first.");
                    await client.SendMessage(chatId, confirmPrompt);
                }
                else
                {
                    await client.SendMessage(chatId, reply);
                }

            }

            // Handle incoming photo messages
            if (update.Message?.Photo != null)
            {
                var chatId = update.Message.Chat.Id;
                if (!_userState.TryGetValue(chatId, out var mode))
                {
                    await client.SendMessage(chatId, "First, select the /insurance command.");
                    return;
                }
                  _userLastMode[chatId] = mode;

                await ProcessPhotoMessage(client, update.Message, mode, token);
            }
            await Task.CompletedTask;
        }

        // Processing photo based on current mode (passport or license)
        private async Task ProcessPhotoMessage(ITelegramBotClient client, Message message, string mode, CancellationToken token)
        {
            var chatId = message.Chat.Id;
            var fileId = message.Photo[^1].FileId; // Get the highest resolution photo
            var file = await client.GetFile(fileId, cancellationToken: token);

            var photosDir = Path.Combine(AppContext.BaseDirectory, "photos");
            Directory.CreateDirectory(photosDir);

            var localFileName = $"{fileId}.jpg";
            var localFilePath = Path.Combine(photosDir, localFileName);

            // Save photo locally
            await using (var fs = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await client.DownloadFile(file.FilePath, fs, cancellationToken: token);
            }

            //Console.WriteLine($"Image saved to {localFilePath}");
            string extractedData;
            if (mode == "passport")
                extractedData = await ProcessPassport(localFilePath, chatId);
            else
                extractedData = await ProcessLicense(localFilePath, chatId);

            // Send extracted data
            var llmResponse = await _openAI.GetResponse(chatId,$"User submitted {mode}. Here is the extracted data:\n{extractedData}\n" + "Formulate a response that the data has been processed");
            await client.SendMessage(chatId, llmResponse, parseMode: ParseMode.Markdown, cancellationToken: token);


            // Switch to next step
            if (mode == "passport")
            {
                _userState[chatId] = "license";
                var nextPrompt = await _openAI.GetResponse(chatId, "The user has already sent a passport photo. Create a polite message asking them to send a photo of their driver's license.");
                await client.SendMessage(chatId, nextPrompt);
            }
            else if (mode == "license")
            {
                var confirmPrompt = await _openAI.GetResponse(chatId,$"The user has sent a driver's license. Previously, the following data was:\n{extractedData}\n" +
                "Create a request for the user to confirm the correctness of the data. Do not forget to mention that this data is important for the formation of an insurance policy.");

                await client.SendMessage(chatId, confirmPrompt, replyMarkup: DataValidationKeyboard);
            }

        }

        // Extract data from passport photo
        private async Task<string> ProcessPassport(string localFilePath,long chatId)
        {
            var apiKey = "2501c82bb5d7ab360cf1cdf13c94cfbd";

            MindeeClient mindeeClient = new MindeeClient(apiKey);

            try
            {
                var inputSource = new LocalInputSource(localFilePath);

                var response = await mindeeClient.ParseAsync<PassportV1>(inputSource);

                // Якщо відповідь успішна, повертаємо дані
                var p = response.Document.Inference.Prediction;
                var surname = p.Surname.Value;
                var givenName = p.GivenNames.First().Value;
                var idNumber = p.IdNumber.Value;
                var birthDate = p.BirthDate.Value;
                var expiryDate = p.ExpiryDate.Value;
                var country = p.Country.Value;
                var gender = p.Gender.Value;
                var birthPlace = p.BirthPlace.Value;

                var userData = new UserDocumentData
                {
                    Surname = surname,
                    GivenName = givenName,
                    PassportIdNumber = idNumber,
                    BirthDate = birthDate,
                    ExpiryDate = expiryDate,
                    Country = country,
                    Gender = gender,
                    BirthPlace = birthPlace
                };

                _userData[chatId] = userData;             // We store data in a ConcurrentDictionary

                return
                    $"📄 **Passport Data:**\n" +
                    $"• Surname: {surname}\n" +
                    $"• Given name: {givenName}\n" +
                    $"• ID number: {idNumber}\n" +
                    $"• Birth date: {birthDate}\n" +
                    $"• Birth place: {birthPlace}\n" +
                    $"• Gender: {gender}\n" +
                    $"• Country: {country}\n" +
                    $"• Expiry date: {expiryDate}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing image: {ex.Message}");
                return await _openAI.GetResponse(chatId, "The user sent a passport photo, but it could not be processed. Please respond politely and instruct the user to resend the photo.");
            }
        }

        // Extract data from driver's license photo
        private async Task<string> ProcessLicense(string localFilePath, long chatId)
        {
            var apiKey = "2501c82bb5d7ab360cf1cdf13c94cfbd";

            MindeeClient mindeeClient = new MindeeClient(apiKey);

            try
            {
                var inputSource = new LocalInputSource(localFilePath);

                var response = await mindeeClient
                     .EnqueueAndParseAsync<DriverLicenseV1>(inputSource);

                var p = response.Document.Inference.Prediction;

                _userData[chatId].DriversLicenseIdNumber = p.Id?.Value;    // We store data in a ConcurrentDictionary

                return
                    $"🚗 **Driver's License Data:**\n" +
                    $"• Category: {p.Category.Value}\n" +
                    $"• ID Number: {p.Id.Value}\n" +
                    $"• Given name: {p.FirstName.Value}\n" +
                    $"• Surname: {p.LastName.Value}\n" +
                    $"• Date of birth: {p.DateOfBirth.Value}\n" +
                    $"• Expiry date: {p.ExpiryDate.Value}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing image: {ex.Message}");
                return await _openAI.GetResponse(chatId, "The user sent a driver's license photo, but it could not be processed. Please respond politely and instruct the user to resend the photo.");
            }
        }

        // Generate insurance policy text
        private string GenerateInsurancePolicy(string firstName, string surName, string licenseId, string passportId)
        {
            return "INSURANCE POLICY\n\n" +
                    $"Policyholder: {firstName} {surName}\n" +
                    $"Passport ID Number: {passportId}\n" +
                    $"Driver's License ID Number: {licenseId}\n" +
                    "Policy Number: POL123456789\n" +
                    "Issue Date: " + DateTime.Now.ToString("yyyy-MM-dd") + "\n" +
                    "Coverage Amount: $100,000\n" +
                    "Price: $100\n\n" +
                    "Thank you for using our service!";
        }

        InlineKeyboardMarkup DataValidationKeyboard = new (new[]             // keyboard for data validation (Correct / Resend)
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Correct", "correct"),
                InlineKeyboardButton.WithCallbackData("Resend", "resend")
            }
        });
        InlineKeyboardMarkup InsuranceConsentKeyboard = new(new[]     // keyboard for insurance consent (Agree / Disagree)
        {
             new[] {
                  InlineKeyboardButton.WithCallbackData("Agree", "agree"),
                  InlineKeyboardButton.WithCallbackData("Disagree", "disagree")
             }
        });
    }
}