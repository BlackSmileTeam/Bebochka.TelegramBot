using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Bebochka.TelegramBot.Handlers;

namespace Bebochka.TelegramBot.Services;

/// <summary>
/// Основной сервис Telegram бота - координирует работу обработчиков
/// </summary>
public class TelegramBotService
{
    private readonly ITelegramBotClient _botClient;
    private readonly ApiClient _apiClient;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly MessageStorageService _messageStorage;
    
    // Обработчики
    private readonly CatalogHandler _catalogHandler;
    private readonly CartHandler _cartHandler;
    private readonly OrderHandler _orderHandler;
    private readonly AdminHandler _adminHandler;
    
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public TelegramBotService(
        ITelegramBotClient botClient,
        ApiClient apiClient,
        ILogger<TelegramBotService> logger,
        MessageStorageService messageStorage,
        CatalogHandler catalogHandler,
        CartHandler cartHandler,
        OrderHandler orderHandler,
        AdminHandler adminHandler)
    {
        _botClient = botClient;
        _apiClient = apiClient;
        _logger = logger;
        _messageStorage = messageStorage;
        _catalogHandler = catalogHandler;
        _cartHandler = cartHandler;
        _orderHandler = orderHandler;
        _adminHandler = adminHandler;
    }

    public async Task StartAsync()
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: _cancellationTokenSource.Token
        );

        var me = await _botClient.GetMeAsync();
        _logger.LogInformation($"Bot @{me.Username} started");
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.Message is { } message)
            {
                await HandleMessageAsync(message, cancellationToken);
            }
            else if (update.CallbackQuery is { } callbackQuery)
            {
                await HandleCallbackQueryAsync(callbackQuery, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update");
        }
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        if (message.Text is not { } messageText)
            return;

        var chatId = message.Chat.Id;
        var userId = message.From?.Id ?? 0;

        _logger.LogInformation($"Received message from {userId}: {messageText}");

        // Check if user is admin via API
        var isAdmin = await _apiClient.IsAdminAsync(userId);

        // Handle commands (starting with /)
        if (messageText.StartsWith("/"))
        {
            var command = messageText.Split(' ')[0].ToLower();
            switch (command)
            {
                case "/start":
                    await SendWelcomeMessageAsync(chatId, isAdmin, cancellationToken);
                    break;
                case "/catalog":
                case "/каталог":
                    // Удаляем сообщение команды
                    try
                    {
                        await _botClient.DeleteMessageAsync(chatId, message.MessageId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete catalog command message");
                    }
                    await _catalogHandler.ShowCatalogAsync(chatId, cancellationToken);
                    break;
                case "/cart":
                case "/корзина":
                    await _cartHandler.ShowCartAsync(chatId, cancellationToken);
                    break;
                case "/orders":
                case "/заказы":
                    await _orderHandler.ShowOrdersAsync(chatId, userId, cancellationToken);
                    break;
                case "/admin":
                    if (isAdmin)
                    {
                        await _adminHandler.ShowAdminPanelAsync(chatId, cancellationToken);
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId,
                            "У вас нет прав администратора.",
                            cancellationToken: cancellationToken);
                    }
                    break;
                default:
                    await _botClient.SendTextMessageAsync(
                        chatId,
                        "Неизвестная команда. Используйте /start для просмотра доступных команд.",
                        cancellationToken: cancellationToken);
                    break;
            }
        }
        else
        {
            // Handle button text messages (keyboard buttons)
            var text = messageText.Trim();
            if (text.Contains("Каталог") || text.Contains("📦"))
            {
                // Удаляем сообщение команды
                try
                {
                    await _botClient.DeleteMessageAsync(chatId, message.MessageId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete catalog command message");
                }
                await _catalogHandler.ShowCatalogAsync(chatId, cancellationToken);
            }
            else if (text.Contains("Корзина") || text.Contains("🛒"))
            {
                await _cartHandler.ShowCartAsync(chatId, cancellationToken);
            }
            else if (text.Contains("Заказы") || text.Contains("📋"))
            {
                await _orderHandler.ShowOrdersAsync(chatId, userId, cancellationToken);
            }
            else if (text.Contains("Админ") || text.Contains("👨‍💼"))
            {
                if (isAdmin)
                {
                    await _adminHandler.ShowAdminPanelAsync(chatId, cancellationToken);
                }
                else
                {
                    await _botClient.SendTextMessageAsync(
                        chatId,
                        "У вас нет прав администратора.",
                        cancellationToken: cancellationToken);
                }
            }
            else
            {
                await _botClient.SendTextMessageAsync(
                    chatId,
                    "Неизвестная команда. Используйте /start для просмотра доступных команд.",
                    cancellationToken: cancellationToken);
            }
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;
        var userId = callbackQuery.From.Id;
        var data = callbackQuery.Data ?? "";

        _logger.LogInformation($"Received callback query: {data}");

        // Answer callback query
        await _botClient.AnswerCallbackQueryAsync(
            callbackQuery.Id,
            cancellationToken: cancellationToken);

        // Parse callback data
        var parts = data.Split(':');
        var action = parts[0];

        switch (action)
        {
            case "product":
                if (parts.Length > 1 && int.TryParse(parts[1], out var productId))
                {
                    await _catalogHandler.ShowProductDetailsAsync(chatId, productId, cancellationToken);
                }
                break;
            case "addtocart":
                if (parts.Length > 1 && int.TryParse(parts[1], out var addProductId))
                {
                    var messageId = callbackQuery.Message?.MessageId ?? 0;
                    await _cartHandler.AddToCartAsync(chatId, userId, addProductId, messageId, cancellationToken);
                }
                break;
            case "removefromcart":
                if (parts.Length > 1 && int.TryParse(parts[1], out var removeCartItemId))
                {
                    await _cartHandler.RemoveFromCartAsync(chatId, removeCartItemId, cancellationToken);
                }
                break;
            case "checkout":
                await _orderHandler.ShowDeliveryMethodSelectionAsync(chatId, userId, cancellationToken);
                break;
            case "delivery_method":
                if (parts.Length > 1)
                {
                    var deliveryMethod = parts[1];
                    await _orderHandler.CreateOrderWithDeliveryAsync(chatId, userId, deliveryMethod, cancellationToken);
                }
                break;
            case "catalog":
                if (callbackQuery.Message != null)
                {
                    try
                    {
                        await _botClient.DeleteMessageAsync(chatId, callbackQuery.Message.MessageId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete catalog button message");
                    }
                }
                await _catalogHandler.ShowCatalogAsync(chatId, cancellationToken);
                break;
            case "cart":
                await _cartHandler.ShowCartAsync(chatId, cancellationToken);
                break;
            case "orders":
                await _orderHandler.ShowOrdersAsync(chatId, userId, cancellationToken);
                break;
            case "order":
                if (parts.Length > 1 && int.TryParse(parts[1], out var orderId))
                {
                    await _orderHandler.ShowOrderDetailsAsync(chatId, userId, orderId, cancellationToken);
                }
                break;
            case "cancelorder":
                if (parts.Length > 1 && int.TryParse(parts[1], out var cancelOrderId))
                {
                    await _orderHandler.CancelOrderAsync(chatId, userId, cancelOrderId, cancellationToken);
                }
                break;
            case "admin":
            case "admin_orders":
                await _adminHandler.ShowAdminOrdersAsync(chatId, cancellationToken);
                break;
            case "admin_order":
                if (parts.Length > 1 && int.TryParse(parts[1], out var adminOrderId))
                {
                    await _adminHandler.ShowAdminOrderDetailsAsync(chatId, adminOrderId, cancellationToken);
                }
                break;
            case "admin_status":
                if (parts.Length > 2 && int.TryParse(parts[1], out var statusOrderId))
                {
                    var newStatus = parts[2];
                    await _adminHandler.UpdateOrderStatusAsync(chatId, statusOrderId, newStatus, cancellationToken);
                }
                break;
            case "admin_stats":
                await _adminHandler.ShowAdminStatisticsAsync(chatId, cancellationToken);
                break;
        }
    }

    private async Task SendWelcomeMessageAsync(long chatId, bool isAdmin, CancellationToken cancellationToken)
    {
        var text = "👋 Добро пожаловать в Bebochka Bot!\n\n" +
                   "Доступные команды:\n" +
                   "/catalog - Каталог товаров\n" +
                   "/cart - Корзина\n" +
                   "/orders - Мои заказы";

        if (isAdmin)
        {
            text += "\n/admin - Панель администратора";
        }

        await _botClient.SendTextMessageAsync(
            chatId,
            text,
            replyMarkup: GetMainMenuKeyboard(isAdmin),
            cancellationToken: cancellationToken);
    }

    private ReplyKeyboardMarkup GetMainMenuKeyboard(bool isAdmin)
    {
        var keyboard = new List<KeyboardButton[]>
        {
            new[] { new KeyboardButton("📦 Каталог"), new KeyboardButton("🛒 Корзина") },
            new[] { new KeyboardButton("📋 Заказы") }
        };

        if (isAdmin)
        {
            keyboard.Add(new[] { new KeyboardButton("👨‍💼 Админ") });
        }

        return new ReplyKeyboardMarkup(keyboard) { ResizeKeyboard = true };
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Polling error occurred");
        return Task.CompletedTask;
    }
}

