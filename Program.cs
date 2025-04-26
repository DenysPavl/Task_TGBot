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
        Host tgBot = new Host(Environment.GetEnvironmentVariable("TELEGRAMBOT_API_KEY"), Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        tgBot.Start();
        Console.ReadLine();
    }
}
