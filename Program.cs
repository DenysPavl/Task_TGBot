using System;
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

/*
using Telegram.Bot;
using Telegram.Bot.Types;
using Microsoft.Extensions.DependencyInjection;
using DotNetEnv;
using Telegram_Task_Bot;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddSingleton<TelegramBotClient>(new TelegramBotClient(Environment.GetEnvironmentVariable("TELEGRAMBOT_API_KEY")));

var app = builder.Build();

// Реєструємо Webhook
var botClient = app.Services.GetRequiredService<TelegramBotClient>();
var webhookUrl = "https://den.42web.io/bot";
await botClient.SetWebhook(webhookUrl);

// Створюємо маршрут для обробки вхідних оновлень
app.MapPost("/bot", async (TelegramBotClient botClient, Update update) =>
{
    // ТУТ ВИКЛИКАЄШ свою UpdateHandler
    var host = new BotHost(botClient, Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
    await host.UpdateHandler(botClient, update, default);
});

app.Run();*/
