using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Bebochka.TelegramBot.Services;

namespace Bebochka.TelegramBot.Handlers;

/// <summary>
/// Базовый класс для всех обработчиков с общими зависимостями
/// </summary>
public abstract class BaseHandler
{
    protected readonly ITelegramBotClient BotClient;
    protected readonly ApiClient ApiClient;
    protected readonly ILogger Logger;
    protected readonly IConfiguration Configuration;
    protected readonly MessageStorageService MessageStorage;

    protected BaseHandler(
        ITelegramBotClient botClient,
        ApiClient apiClient,
        ILogger logger,
        IConfiguration configuration,
        MessageStorageService messageStorage)
    {
        BotClient = botClient;
        ApiClient = apiClient;
        Logger = logger;
        Configuration = configuration;
        MessageStorage = messageStorage;
    }
}

