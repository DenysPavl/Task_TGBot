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

builder.WebHost.UseUrls($"http://0.0.0.0:300");
builder.Services.AddHttpClient();
builder.Services.AddSingleton<TelegramBotClient>(new TelegramBotClient(Environment.GetEnvironmentVariable("TELEGRAMBOT_API_KEY")));

var app = builder.Build();

// Створення екземпляра Host
var host = new BotHost(Environment.GetEnvironmentVariable("TELEGRAMBOT_API_KEY"), Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

// Налаштування Webhook
var webhookUrl = "https://tasktgbot-production.up.railway.app/bot";  // Тут має бути правильний URL
await host.SetWebhook(webhookUrl);

// Створюємо маршрут для обробки вхідних оновлень
app.MapPost("/bot", async (TelegramBotClient botClient, Update update) =>
{
    await host.UpdateHandler(botClient, update, default);
});

app.Run();

