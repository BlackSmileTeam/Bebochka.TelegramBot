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

    private static readonly string[] ReserveWords = { "мне", "я", "беру", "бронь" };

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
            // Логируем каждый входящий апдейт. Комментарии по кнопке «Прокомментировать» приходят как Message из группы обсуждения (не как ChannelPost).
            long logChatId = 0;
            var logChatType = "?";
            var logText = "(no text)";
            string updateKind;

            if (update.Message != null)
            {
                updateKind = "Message";
                logChatId = update.Message.Chat.Id;
                logChatType = update.Message.Chat.Type.ToString();
                logText = update.Message.Text ?? "(no text)";
            }
            else if (update.CallbackQuery != null)
            {
                updateKind = "CallbackQuery";
                logChatId = update.CallbackQuery.Message?.Chat?.Id ?? 0;
                logChatType = update.CallbackQuery.Message?.Chat?.Type.ToString() ?? "?";
                logText = update.CallbackQuery.Data ?? "(no data)";
            }
            else if (update.ChannelPost != null)
            {
                updateKind = "ChannelPost";
                logChatId = update.ChannelPost.Chat.Id;
                logChatType = update.ChannelPost.Chat.Type.ToString();
                logText = update.ChannelPost.Text ?? update.ChannelPost.Caption ?? "(post in channel)";
                _logger.LogInformation("Update received: Id={UpdateId}, Kind={Kind}, ChatId={ChatId}. Это пост в канале. Комментарии «беру» приходят из группы обсуждения как Message — добавьте бота в группу обсуждения.",
                    update.Id, updateKind, logChatId);
            }
            else if (update.EditedChannelPost != null)
            {
                updateKind = "EditedChannelPost";
                logChatId = update.EditedChannelPost.Chat.Id;
                logChatType = update.EditedChannelPost.Chat.Type.ToString();
                logText = update.EditedChannelPost.Text ?? "(edited post)";
            }
            else
            {
                updateKind = "Other";
            }

            if (updateKind != "ChannelPost")
            {
                _logger.LogInformation("Update received: Id={UpdateId}, Kind={Kind}, ChatId={ChatId}, ChatType={ChatType}, Text={Text}",
                    update.Id, updateKind, logChatId, logChatType, logText);
            }

            if (update.Message is { } message)
            {
                if (message.Chat.Type is ChatType.Supergroup or ChatType.Group)
                    _logger.LogInformation("Сообщение из группы (ChatId={ChatId}). Если это группа обсуждения канала — ответы «беру» здесь будут обрабатываться.", message.Chat.Id);
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

        if (userId == 0)
            return;

        _logger.LogInformation($"Received message from {userId}: {messageText}");

        // Бронь по первому сообщению под постом в канале или в группе обсуждения (слова: мне, я, беру, бронь)
        if (message.ReplyToMessage != null)
        {
            var textLower = messageText.Trim().ToLowerInvariant();
            if (ReserveWords.Any(w => textLower.Contains(w)))
            {
                var replyTo = message.ReplyToMessage;
                var (channelId, messageId) = GetChannelPostFromReply(replyTo);

                _logger.LogInformation(
                    "Reserve check: ChatType={ChatType}, ChatId={ChatId}, ReplyToMessageId={ReplyToMsgId}, ReplyToMessage.ChatId={ReplyChatId}, ReplyToMessage.SenderChat={SenderChatId}, Resolved ChannelId={ResolvedChannelId}, MessageId={ResolvedMessageId}",
                    message.Chat.Type, message.Chat.Id, replyTo.MessageId, replyTo.Chat?.Id, replyTo.SenderChat?.Id, channelId ?? "(null)", messageId?.ToString() ?? "(null)");

                if (channelId != null && messageId != null)
                {
                    _logger.LogInformation("Reserve attempt: reply under post ChannelId={ChannelId}, MessageId={MessageId}, UserId={UserId}", channelId, messageId.Value, userId);
                    try
                    {
                        await _apiClient.RegisterTelegramUserAsync(userId);
                    }
                    catch { /* ignore */ }

                    var phone = message.Contact?.PhoneNumber;
                    var result = await _apiClient.ReserveFromTelegramAsync(
                        channelId,
                        messageId.Value,
                        userId,
                        message.From?.Username,
                        message.From?.FirstName,
                        message.From?.LastName,
                        customerPhone: phone);

                    if (result != null)
                    {
                        if (!result.Success)
                            _logger.LogWarning("Reserve failed: ChannelId={ChannelId}, MessageId={MessageId}, Reason={Reason}", channelId, messageId.Value, result.Reason ?? "unknown");
                        if (result.Success && result.Order != null)
                        {
                            // Успешная бронь — в комментариях не пишем сообщение пользователю
                        }
                        else
                        {
                            var reasonText = result.Reason switch
                            {
                                "AlreadyReserved" => "Этот товар уже забронирован.",
                                "UserNotFound" => "Напишите боту в личку /start, затем снова напишите «беру» под постом.",
                                "ProductNotFound" => "Этот пост не привязан к товару.",
                                "OutOfStock" => "Товар закончился.",
                                _ => "Не удалось забронировать."
                            };
                            await _botClient.SendTextMessageAsync(
                                chatId,
                                $"❌ {reasonText}",
                                replyToMessageId: message.MessageId,
                                cancellationToken: cancellationToken);
                        }
                        return;
                    }
                }
                else
                {
                    LogReplyToMessageStructure(replyTo);
                }
            }
        }

        // Automatically register Telegram user for notifications
        try
        {
            await _apiClient.RegisterTelegramUserAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to register Telegram user {userId}");
            // Continue execution even if registration fails
        }

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

        // Automatically register Telegram user for notifications
        try
        {
            await _apiClient.RegisterTelegramUserAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to register Telegram user {userId}");
            // Continue execution even if registration fails
        }

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

    /// <summary>
    /// Из ответа на сообщение получает идентификаторы поста в канале (channelId, messageId) для привязки к товару.
    /// Если ответ в канале — берём Chat.Id и MessageId. Если ответ в группе обсуждения — пост приходит пересланным, берём origin channel/message_id.
    /// </summary>
    private static (string? channelId, int? messageId) GetChannelPostFromReply(Message replyToMessage)
    {
        // Пересланное из канала (комментарий в группе обсуждения): приоритет — id канала и id сообщения в канале
        var (originChatId, originMessageId) = TryGetForwardOriginChannel(replyToMessage);
        if (originChatId != null && originMessageId != null)
            return (originChatId, originMessageId);
        // Ответ прямо в канале под постом
        return (replyToMessage.Chat.Id.ToString(), replyToMessage.MessageId);
    }

    /// <summary>
    /// Пытается получить (channelId, messageId) из данных о пересылке (ForwardOrigin или ForwardFromChat/ForwardFromMessageId)
    /// или из SenderChat (сообщение «от канала» в группе обсуждения).
    /// </summary>
    private static (string? channelId, int? messageId) TryGetForwardOriginChannel(Message message)
    {
        if (message == null) return (null, null);
        var type = message.GetType();

        // ForwardOrigin (Bot API 7.0+): MessageOriginChannel с Chat и MessageId
        var forwardOriginProp = type.GetProperty("ForwardOrigin");
        if (forwardOriginProp?.GetValue(message) is { } origin)
        {
            var originType = origin.GetType();
            var chatProp = originType.GetProperty("Chat");
            var msgIdProp = originType.GetProperty("MessageId");
            if (chatProp?.GetValue(origin) is Chat originChat && msgIdProp?.GetValue(origin) is int msgId)
                return (originChat.Id.ToString(), msgId);
        }

        // Устаревшие поля: ForwardFromChat, ForwardFromMessageId
        var forwardFromChatProp = type.GetProperty("ForwardFromChat");
        var forwardFromMessageIdProp = type.GetProperty("ForwardFromMessageId");
        if (forwardFromChatProp?.GetValue(message) is Chat fromChat && forwardFromMessageIdProp?.GetValue(message) is int fromMsgId)
            return (fromChat.Id.ToString(), fromMsgId);

        // Группа обсуждения: сообщение может быть «от канала» (SenderChat = канал), но message_id поста в канале только в forward
        var senderChatProp = type.GetProperty("SenderChat");
        if (senderChatProp?.GetValue(message) is Chat senderChat && senderChat.Type == ChatType.Channel)
        {
            var channelIdStr = senderChat.Id.ToString();
            // message_id в канале по-прежнему нужен; пробуем только forward_from_message_id
            if (forwardFromMessageIdProp?.GetValue(message) is int fwdMsgId)
                return (channelIdStr, fwdMsgId);
            // Без message_id в канале заказ не создать — не возвращаем только channelId
        }

        return (null, null);
    }

    private void LogReplyToMessageStructure(Message replyTo)
    {
        if (replyTo == null) return;
        var t = replyTo.GetType();
        var fo = t.GetProperty("ForwardOrigin")?.GetValue(replyTo);
        var ffc = t.GetProperty("ForwardFromChat")?.GetValue(replyTo);
        var ffmi = t.GetProperty("ForwardFromMessageId")?.GetValue(replyTo);
        _logger.LogWarning(
            "Reserve skipped: could not resolve channel post from reply. Ensure: 1) Bot is in the discussion group, 2) User replied via Comment to the channel post. ReplyToMessage: ForwardOrigin={FO}, ForwardFromChat={FFC}, ForwardFromMessageId={FFMI}, SenderChat={SenderChat}",
            fo?.GetType().Name ?? "null", ffc is Chat fc ? fc.Id.ToString() : "null", ffmi?.ToString() ?? "null", replyTo.SenderChat?.Id.ToString() ?? "null");
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Polling error occurred");
        return Task.CompletedTask;
    }
}

