using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Bebochka.TelegramBot.Services;

namespace Bebochka.TelegramBot.Handlers;

/// <summary>
/// Обработчик для работы с корзиной
/// </summary>
public class CartHandler : BaseHandler
{
    public CartHandler(
        ITelegramBotClient botClient,
        ApiClient apiClient,
        ILogger<CartHandler> logger,
        IConfiguration configuration,
        MessageStorageService messageStorage)
        : base(botClient, apiClient, logger, configuration, messageStorage)
    {
    }

    public async Task ShowCartAsync(long chatId, CancellationToken cancellationToken)
    {
        var sessionId = chatId.ToString();
        var cartItems = await ApiClient.GetCartItemsAsync(sessionId);

        if (!cartItems.Any())
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "🛒 Ваша корзина пуста.",
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("📦 Каталог", "catalog") }
                }),
                cancellationToken: cancellationToken);
            return;
        }

        var text = "🛒 Ваша корзина:\n\n";
        var total = 0m;
        var keyboard = new List<List<InlineKeyboardButton>>();

        var isFirst = true;
        foreach (var item in cartItems)
        {
            if (!isFirst)
            {
                text += "─────────────\n\n";
            }
            isFirst = false;

            var subtotal = item.ProductPrice * item.Quantity;
            total += subtotal;
            text += $"📦 {item.ProductName}\n";
            text += $"   Количество: {item.Quantity} шт.\n";
            text += $"   Цена: {item.ProductPrice:N0} ₽\n";
            text += $"   Сумма: {subtotal:N0} ₽\n\n";

            keyboard.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData($"❌ Удалить {item.ProductName}", $"removefromcart:{item.Id}")
            });
        }

        text += $"💰 Итого: {total:N0} ₽\n";

        keyboard.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("✅ Оформить заказ", "checkout")
        });
        keyboard.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("📦 Каталог", "catalog")
        });

        await BotClient.SendTextMessageAsync(
            chatId,
            text,
            replyMarkup: new InlineKeyboardMarkup(keyboard),
            cancellationToken: cancellationToken);
    }

    public async Task AddToCartAsync(long chatId, long userId, int productId, int buttonMessageId, CancellationToken cancellationToken)
    {
        var sessionId = chatId.ToString();
        var success = await ApiClient.AddToCartAsync(sessionId, productId, 1);

        if (success)
        {
            // Находим и удаляем сообщения с товаром только при успешном добавлении
            var productMessageIds = MessageStorage.GetProductMessages(chatId, productId);
            if (productMessageIds != null && productMessageIds.Any())
            {
                // Удаляем все сообщения товара (медиа-группа + кнопки)
                foreach (var msgId in productMessageIds)
                {
                    try
                    {
                        await BotClient.DeleteMessageAsync(chatId, msgId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, $"Failed to delete product message {msgId}");
                    }
                }

                // Удаляем из списка сообщений каталога
                MessageStorage.RemoveCatalogMessages(chatId, productMessageIds);

                // Удаляем из связи productId -> messageId
                MessageStorage.RemoveProductMessages(chatId, productId);
            }
            else
            {
                // Если не нашли в словаре, пытаемся удалить сообщение с кнопкой и несколько предыдущих
                try
                {
                    await BotClient.DeleteMessageAsync(chatId, buttonMessageId, cancellationToken);
                    for (int i = 1; i <= 3; i++)
                    {
                        try
                        {
                            await BotClient.DeleteMessageAsync(chatId, buttonMessageId - i, cancellationToken);
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, $"Failed to delete messages around {buttonMessageId}");
                }
            }

            // Отправляем сообщение об успехе с кнопкой перехода в корзину
            var successKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🛒 Перейти в корзину", "cart")
                }
            });

            await BotClient.SendTextMessageAsync(
                chatId,
                "✅ Товар добавлен в корзину!",
                replyMarkup: successKeyboard,
                cancellationToken: cancellationToken);
        }
        else
        {
            // При неудаче - просто показываем ошибку, сообщения товара оставляем
            await BotClient.SendTextMessageAsync(
                chatId,
                "❌ Не удалось добавить товар в корзину. Возможно, товар закончился.",
                cancellationToken: cancellationToken);
        }
    }

    public async Task RemoveFromCartAsync(long chatId, int cartItemId, CancellationToken cancellationToken)
    {
        var success = await ApiClient.RemoveFromCartAsync(cartItemId);

        if (success)
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "✅ Товар удален из корзины.",
                cancellationToken: cancellationToken);

            await ShowCartAsync(chatId, cancellationToken);
        }
        else
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "❌ Не удалось удалить товар из корзины.",
                cancellationToken: cancellationToken);
        }
    }
}

