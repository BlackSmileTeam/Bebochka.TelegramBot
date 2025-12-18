using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Bebochka.TelegramBot.Models.DTOs;
using Bebochka.TelegramBot.Services;

namespace Bebochka.TelegramBot.Handlers;

/// <summary>
/// Обработчик для работы с заказами
/// </summary>
public class OrderHandler : BaseHandler
{
    public OrderHandler(
        ITelegramBotClient botClient,
        ApiClient apiClient,
        ILogger<OrderHandler> logger,
        IConfiguration configuration,
        MessageStorageService messageStorage)
        : base(botClient, apiClient, logger, configuration, messageStorage)
    {
    }

    public async Task ShowDeliveryMethodSelectionAsync(long chatId, long userId, CancellationToken cancellationToken)
    {
        var sessionId = chatId.ToString();
        var cartItems = await ApiClient.GetCartItemsAsync(sessionId);

        if (!cartItems.Any())
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "Корзина пуста.",
                cancellationToken: cancellationToken);
            return;
        }

        var total = cartItems.Sum(item => item.ProductPrice * item.Quantity);

        var text = "📦 Оформление заказа\n\n";
        text += $"💰 Сумма заказа: {total:N0} ₽\n";
        text += $"📦 Товаров: {cartItems.Sum(item => item.Quantity)} шт.\n\n";
        text += "Выберите способ доставки:";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🚚 Курьерская доставка", "delivery_method:Курьерская доставка")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📮 Почта России", "delivery_method:Почта России")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🏪 Самовывоз", "delivery_method:Самовывоз")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⬅️ Назад в корзину", "cart")
            }
        });

        await BotClient.SendTextMessageAsync(
            chatId,
            text,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    public async Task CreateOrderWithDeliveryAsync(long chatId, long userId, string deliveryMethod, CancellationToken cancellationToken)
    {
        var sessionId = chatId.ToString();
        var cartItems = await ApiClient.GetCartItemsAsync(sessionId);

        if (!cartItems.Any())
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "Корзина пуста.",
                cancellationToken: cancellationToken);
            return;
        }

        var orderDto = new CreateOrderDto
        {
            SessionId = sessionId,
            UserId = (int)userId,
            CustomerName = $"Telegram User {userId}",
            CustomerPhone = "",
            DeliveryMethod = deliveryMethod,
            Items = cartItems.Select(item => new CreateOrderItemDto
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity
            }).ToList()
        };

        var order = await ApiClient.CreateOrderAsync(orderDto);

        if (order != null)
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                $"✅ Заказ #{order.OrderNumber} успешно оформлен!\n\n" +
                $"💰 Сумма: {order.TotalAmount:N0} ₽\n" +
                $"📦 Товаров: {order.OrderItems.Sum(oi => oi.Quantity)} шт.\n" +
                $"🚚 Способ доставки: {deliveryMethod}\n\n" +
                $"Используйте /orders для просмотра статуса заказа.",
                cancellationToken: cancellationToken);
        }
        else
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "❌ Не удалось оформить заказ. Попробуйте позже.",
                cancellationToken: cancellationToken);
        }
    }

    public async Task ShowOrdersAsync(long chatId, long userId, CancellationToken cancellationToken)
    {
        var orders = await ApiClient.GetUserOrdersAsync((int)userId);

        if (!orders.Any())
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "У вас пока нет заказов.",
                cancellationToken: cancellationToken);
            return;
        }

        var text = "📋 Ваши заказы:\n\n";
        var keyboard = new List<List<InlineKeyboardButton>>();

        foreach (var order in orders.Take(10))
        {
            text += $"📦 {order.OrderNumber}\n";
            text += $"📅 {order.CreatedAt:dd.MM.yyyy HH:mm}\n";
            text += $"💰 {order.TotalAmount:N0} ₽\n";
            text += $"📊 Статус: {order.Status}\n\n";

            keyboard.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData($"📋 {order.OrderNumber}", $"order:{order.Id}")
            });
        }

        await BotClient.SendTextMessageAsync(
            chatId,
            text,
            replyMarkup: new InlineKeyboardMarkup(keyboard),
            cancellationToken: cancellationToken);
    }

    public async Task ShowOrderDetailsAsync(long chatId, long userId, int orderId, CancellationToken cancellationToken)
    {
        var order = await ApiClient.GetOrderByIdAsync(orderId);

        if (order == null)
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "Заказ не найден.",
                cancellationToken: cancellationToken);
            return;
        }

        var text = $"📦 Заказ {order.OrderNumber}\n\n";
        text += $"📅 Дата: {order.CreatedAt:dd.MM.yyyy HH:mm}\n";
        text += $"📊 Статус: {order.Status}\n";
        if (!string.IsNullOrEmpty(order.DeliveryMethod))
        {
            text += $"🚚 Способ доставки: {order.DeliveryMethod}\n";
        }
        text += $"💰 Сумма: {order.TotalAmount:N0} ₽\n\n";
        text += "Товары:\n";

        foreach (var item in order.OrderItems)
        {
            text += $"  • {item.ProductName} - {item.Quantity} шт. × {item.ProductPrice:N0} ₽\n";
        }

        var keyboard = new List<List<InlineKeyboardButton>>();

        if (order.Status != "Доставлен" && order.Status != "Отменен")
        {
            keyboard.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("❌ Отменить заказ", $"cancelorder:{order.Id}")
            });
        }

        keyboard.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("📋 Все заказы", "orders")
        });

        await BotClient.SendTextMessageAsync(
            chatId,
            text,
            replyMarkup: new InlineKeyboardMarkup(keyboard),
            cancellationToken: cancellationToken);
    }

    public async Task CancelOrderAsync(long chatId, long userId, int orderId, CancellationToken cancellationToken)
    {
        var success = await ApiClient.CancelOrderAsync(orderId);

        if (success)
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                $"✅ Заказ успешно отменен.",
                cancellationToken: cancellationToken);
        }
        else
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "❌ Не удалось отменить заказ.",
                cancellationToken: cancellationToken);
        }
    }
}

