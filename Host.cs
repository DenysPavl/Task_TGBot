using Telegram.Bot;
using Telegram.Bot.Polling;
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


namespace Telegram_Task_Bot
{
    class Host
    {
        TelegramBotClient _bot;
        private readonly OpenAIChat _openAI;
        private static readonly ConcurrentDictionary<long, string> _userState = new ConcurrentDictionary<long, string>();
        private static readonly ConcurrentDictionary<long, string> _userLastMode = new();
        private static readonly ConcurrentDictionary<long, UserDocumentData> _userData = new ConcurrentDictionary <long, UserDocumentData>();

        public Host(string token, string openAI_key)
        {
            _bot = new TelegramBotClient(token);
            _openAI = new OpenAIChat(openAI_key);
        }
        public async Task SetWebhookAsync(string webhookUrl)
        {
            await _bot.SetWebhook(webhookUrl);
            Console.WriteLine($"Webhook set to: {webhookUrl}");
        }


        private async Task ErrorHandler(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken token)
        {
            Console.WriteLine("Error: " + exception.Message);
            await Task.CompletedTask;
        }

        public async Task UpdateHandler(ITelegramBotClient client, Update update, CancellationToken token)
        {
            Console.WriteLine("Received update");
           if (update.CallbackQuery != null)
            {
                var chatId = update.CallbackQuery.Message.Chat.Id;
                var callbackData = update.CallbackQuery.Data;

                if (callbackData == "correct")
                {
                    await client.EditMessageReplyMarkup(chatId: chatId, messageId: update.CallbackQuery.Message.MessageId); // Зникає клавіатура
                    await client.SendMessage(chatId, "Ціна страховки — $100. Ви згодні?", replyMarkup: InsuranceConsentKeyboard);
                }
                else if (callbackData == "resend")
                {
                    await client.EditMessageReplyMarkup(chatId: chatId, messageId: update.CallbackQuery.Message.MessageId); // Зникає клавіатура
                    _userState[chatId] = "passport";
                    await client.SendMessage(chatId, "Будь ласка, надішліть фотографію паспорта ще раз.");
                }
                else if (callbackData == "agree")
                {
                    await client.EditMessageReplyMarkup(chatId: chatId, messageId: update.CallbackQuery.Message.MessageId); // Зникає клавіатура

                    if (string.IsNullOrEmpty(_userData[chatId].PassportIdNumber))
                    {
                        _userState[chatId] = "passport";
                        await client.SendMessage(chatId, "Сталася помилка з даними з вашого паспорта! Будь ласка, надішліть фотографію вашого паспорта.");
                    }
                    else if (string.IsNullOrEmpty(_userData[chatId].DriversLicenseIdNumber))
                    {
                        _userState[chatId] = "license";
                        await client.SendMessage(chatId, "Сталася помилка з даними з ваших водійських прав! Будь ласка, надішліть фотографію ваших водійських прав.");
                    }
                    else 
                        await client.SendMessage(chatId, $"📄 Ваш страховий поліс:\n\n{GenerateInsurancePolicy(_userData[chatId].GivenName, _userData[chatId].Surname, _userData[chatId].PassportIdNumber, _userData[chatId].DriversLicenseIdNumber)}");
                }
                else if (callbackData == "disagree")
                {
                    await client.SendMessage(chatId, "Вибачте, але $100 — єдина доступна ціна.");
                }
                return;
            }

            if (update.Message == null)
                return;
            
            Console.WriteLine(update.Message?.Text);
            if (update.Message?.Text != null)
            {
                var chatId = update.Message.Chat.Id;
                var text = update.Message.Text.Trim().ToLower();
                switch (text)
                {
                    case "/start":
                        await client.SendMessage(chatId,
                            "Привіт! Це бот для придбання автостраховки.\n" +
                            "Команди:\n" +
                            "/insurance - початок оформлення автостраховки.");
                        return;
                    case "/insurance":
                        _userState[chatId] = "passport";
                        _userData[chatId] = new UserDocumentData(); // Обнуляємо попередні дані
                        await client.SendMessage(chatId, "Починаємо оформлення автостраховки. Спершу, надішліть фото паспорта.");
                        return;
                    default:
                        var reply = await _openAI.GetResponse(text);
                        await client.SendMessage(chatId, reply);
                        break;
                }
            }
            
            if (update.Message?.Photo != null)
            {
                var chatId = update.Message.Chat.Id;
                if (!_userState.TryGetValue(chatId, out var mode))
                {
                    await client.SendMessage(chatId, "Будь ласка, виберіть спочатку команду /insurance");
                    return;
                }
                  _userLastMode[chatId] = mode;

                await ProcessPhotoMessage(client, update.Message, mode, token);
            }
            await Task.CompletedTask;
        }
        private async Task ProcessPhotoMessage(ITelegramBotClient client, Message message, string mode, CancellationToken token)
        {
            var chatId = message.Chat.Id;
            var fileId = message.Photo[^1].FileId;
            var file = await client.GetFile(fileId, cancellationToken: token);

            var photosDir = Path.Combine(AppContext.BaseDirectory, "photos");
            Directory.CreateDirectory(photosDir);

            var localFileName = $"{fileId}.jpg";
            var localFilePath = Path.Combine(photosDir, localFileName);

            await using (var fs = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await client.DownloadFile(file.FilePath, fs, cancellationToken: token);
            }

            Console.WriteLine($"Image saved to {localFilePath}");

            string extractedData;
            if (mode == "passport")
                extractedData = await ProcessPassport(localFilePath, chatId);
            else
                extractedData = await ProcessLicense(localFilePath, chatId);

            await client.SendMessage(chatId, $"Document processed. Extracted data: {extractedData} " + ParseMode.Markdown);

            if (mode == "passport")
            {
                _userState[chatId] = "license";
                await client.SendMessage(chatId, "Дякуємо! Тепер, будь ласка, надішліть фото водійського посвідчення.");
            }
            else if (mode == "license")
            {
                await client.SendMessage(chatId, $"Будь ласка, підтвердіть правильність даних вище:", replyMarkup: DataValidationKeyboard);
            }

        }

        private async Task<string> ProcessPassport(string localFilePath,long chatId)
        {
            var apiKey = "4c74ae489bac335b569bb4771391c646";

            MindeeClient mindeeClient = new MindeeClient(apiKey);

            try
            {
                var inputSource = new LocalInputSource(localFilePath);

                var response = await mindeeClient
                    .ParseAsync<PassportV1>(inputSource);

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

                _userData[chatId] = userData;             // Зберігаємо дані в ConcurrentDictionary

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
                return $"Error processing image: {ex.Message}";
            }
        }
        private async Task<string> ProcessLicense(string localFilePath, long chatId)
        {
            var apiKey = "4c74ae489bac335b569bb4771391c646";

            MindeeClient mindeeClient = new MindeeClient(apiKey);

            try
            {
                var inputSource = new LocalInputSource(localFilePath);

                var response = await mindeeClient
                     .EnqueueAndParseAsync<DriverLicenseV1>(inputSource);

                var p = response.Document.Inference.Prediction;   // Якщо відповідь успішна, повертаємо дані

                _userData[chatId].DriversLicenseIdNumber = p.Id.Value;                // Зберігаємо дані в ConcurrentDictionary

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
                return $"Error processing image: {ex.Message}";
            }
        }

        private string GenerateInsurancePolicy(string firstName, string surName, string licenseId, string passportId)
        {
            return "СТРАХОВИЙ ПОЛІС\n\n" +
                   $"Страхувальник: {firstName} {surName}\n" +
                   $"Passport ID Number: {passportId}\n" +
                   $"Driver's License ID Number: {licenseId}\n" +
                   "Номер поліса: POL123456789\n" +
                   "Дата видачі: " + DateTime.Now.ToString("yyyy-MM-dd") + "\n" +
                   "Сума покриття: $100,000\n" +
                   "Ціна: $100\n\n" +
                   "Дякуємо за користування нашим сервісом!";
        }

        InlineKeyboardMarkup DataValidationKeyboard = new (new[]             // keyboard for data validation
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Все вірно", "correct"),
                InlineKeyboardButton.WithCallbackData("Надіслати наново", "resend")
            }
        });
        InlineKeyboardMarkup InsuranceConsentKeyboard = new(new[]     // keyboard for insurance consent
        {
             new[] {
                  InlineKeyboardButton.WithCallbackData("Згоден", "agree"),
                  InlineKeyboardButton.WithCallbackData("Не згоден", "disagree")
             }
        });
    }
}