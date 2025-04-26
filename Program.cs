/*using System;
using Telegram_Task_Bot;
using DotNetEnv;

class Program
{
    static void Main(string[] args)
    {
        Env.Load();
        string telegramToken = Environment.GetEnvironmentVariable("TELEGRAMBOT_API_KEY");

        if (string.IsNullOrEmpty(telegramToken))
        {
            Console.WriteLine("TELEGRAM TOKEN is missing!");
        }
        else
        {
            Console.WriteLine("TELEGRAM TOKEN found!");
        }

        BotHost tgBot = new BotHost(Environment.GetEnvironmentVariable("TELEGRAMBOT_API_KEY"), Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        tgBot.Start();
        Console.ReadLine();
    }
}
*/

using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram_Task_Bot;
using DotNetEnv;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Беремо порт з середовища або ставимо дефолтний
var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";

// Сервіси
builder.Services.AddHttpClient();
builder.Services.AddSingleton<TelegramBotClient>(new TelegramBotClient(Environment.GetEnvironmentVariable("TELEGRAMBOT_API_KEY")));

var app = builder.Build();

// Додаємо правильні URL-и
app.Urls.Add($"http://0.0.0.0:{port}");

// Створення екземпляра бота
// Створення екземпляра Host
var host = new BotHost(Environment.GetEnvironmentVariable("TELEGRAMBOT_API_KEY"), Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

// Скидання вебхука
await host.SetWebhook(""); // Спочатку очищаємо існуючий вебхук

// Налаштування нового вебхука
var webhookUrl = "https://tasktgbot-production.up.railway.app/bot";
await host.SetWebhook(webhookUrl);

// Обробник Webhook запиту
app.MapPost("/bot", async (HttpRequest request, TelegramBotClient botClient) =>
{
    try
    {
        var update = await request.ReadFromJsonAsync<Update>();

        if (update != null)
        {
            Console.WriteLine("Received update:");
            Console.WriteLine(update); // Логувати сам об'єкт update

            await host.UpdateHandler(botClient, update, default);
        }
        else
        {
            Console.WriteLine("No update received.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error processing update: {ex.Message}");
    }

    return Results.Ok(); // Завжди повертаємо 200 OK
});


app.Run();


