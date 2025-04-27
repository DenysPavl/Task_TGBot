using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram_Task_Bot;
using DotNetEnv;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

// We take the port from the environment or set the default
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";

// Services
builder.Services.AddHttpClient();
builder.Services.AddSingleton<TelegramBotClient>(new TelegramBotClient(Environment.GetEnvironmentVariable("TELEGRAMBOT_API_KEY")));

var app = builder.Build();

// Adding the correct URLs
app.Urls.Add($"http://0.0.0.0:{port}");

// Creating a bot instance
var host = new BotHost(Environment.GetEnvironmentVariable("TELEGRAMBOT_API_KEY"), Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

// Clear existing webhook
await host.SetWebhook("");

// Setting up a new webhook
var webhookUrl = "https://tasktgbot-production.up.railway.app/bot";
await host.SetWebhook(webhookUrl);

// Webhook request handler
app.MapPost("/bot", async (HttpRequest request, TelegramBotClient botClient) =>
{
    try
    {
        var update = await request.ReadFromJsonAsync<Update>();

        if (update != null)
        {
            await host.UpdateHandler(botClient, update, default);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error processing update: {ex.Message}");
    }

    return Results.Ok();
});

app.Run();


