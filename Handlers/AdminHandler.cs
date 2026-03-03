using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Bebochka.TelegramBot.Services;

namespace Bebochka.TelegramBot.Handlers;

/// <summary>
/// Обработчик для административных функций
/// </summary>
public class AdminHandler : BaseHandler
{
    public AdminHandler(
        ITelegramBotClient botClient,
        ApiClient apiClient,
        ILogger<AdminHandler> logger,
        IConfiguration configuration,
        MessageStorageService messageStorage)
        : base(botClient, apiClient, logger, configuration, messageStorage)
    {
    }

    public async Task ShowAdminPanelAsync(long chatId, CancellationToken cancellationToken)
    {
        var text = "👨‍💼 Панель администратора\n\n" +
                   "Выберите действие:";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📋 Заказы", "admin_orders")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Статистика", "admin_stats")
            }
        });

        await BotClient.SendTextMessageAsync(
            chatId,
            text,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    public async Task ShowAdminOrdersAsync(long chatId, CancellationToken cancellationToken)
    {
        var orders = await ApiClient.GetAllOrdersAsync();

        if (!orders.Any())
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "Заказы не найдены.",
                cancellationToken: cancellationToken);
            return;
        }

        var text = "📋 Все заказы:\n\n";
        var keyboard = new List<List<InlineKeyboardButton>>();

        foreach (var order in orders.Take(20))
        {
            text += $"📦 {order.OrderNumber}\n";
            text += $"👤 {order.CustomerName}\n";
            text += $"📅 {order.CreatedAt:dd.MM.yyyy HH:mm}\n";
            text += $"💰 {order.TotalAmount:N0} ₽\n";
            text += $"📊 {order.Status}\n\n";

            keyboard.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData($"📋 {order.OrderNumber}", $"admin_order:{order.Id}")
            });
        }

        await BotClient.SendTextMessageAsync(
            chatId,
            text,
            replyMarkup: new InlineKeyboardMarkup(keyboard),
            cancellationToken: cancellationToken);
    }

    public async Task ShowAdminOrderDetailsAsync(long chatId, int orderId, CancellationToken cancellationToken)
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
        text += $"👤 Клиент: {order.CustomerName}\n";
        text += $"📞 Телефон: {order.CustomerPhone}\n";
        if (!string.IsNullOrEmpty(order.CustomerEmail))
            text += $"📧 Email: {order.CustomerEmail}\n";
        text += $"📅 Дата: {order.CreatedAt:dd.MM.yyyy HH:mm}\n";
        text += $"📊 Статус: {order.Status}\n";
        if (!string.IsNullOrEmpty(order.DeliveryMethod))
            text += $"🚚 Доставка: {order.DeliveryMethod}\n";
        text += $"💰 Сумма: {order.TotalAmount:N0} ₽\n\n";
        text += "Товары:\n";

        foreach (var item in order.OrderItems)
        {
            text += $"  • {item.ProductName} - {item.Quantity} шт. × {item.ProductPrice:N0} ₽\n";
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("В сборке", $"admin_status:{order.Id}:В сборке"),
                InlineKeyboardButton.WithCallbackData("Ожидает оплату", $"admin_status:{order.Id}:Ожидает оплату")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("В пути", $"admin_status:{order.Id}:В пути"),
                InlineKeyboardButton.WithCallbackData("Доставлен", $"admin_status:{order.Id}:Доставлен")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Отменен", $"admin_status:{order.Id}:Отменен")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("⬅️ Назад", "admin_orders")
            }
        });

        await BotClient.SendTextMessageAsync(
            chatId,
            text,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    public async Task UpdateOrderStatusAsync(long chatId, int orderId, string newStatus, CancellationToken cancellationToken)
    {
        var success = await ApiClient.UpdateOrderStatusAsync(orderId, newStatus);

        if (success)
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                $"✅ Статус заказа изменен на: {newStatus}",
                cancellationToken: cancellationToken);

            await ShowAdminOrderDetailsAsync(chatId, orderId, cancellationToken);
        }
        else
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "❌ Не удалось изменить статус заказа.",
                cancellationToken: cancellationToken);
        }
    }

    public async Task ShowAdminStatisticsAsync(long chatId, CancellationToken cancellationToken)
    {
        var stats = await ApiClient.GetStatisticsAsync();

        if (stats == null)
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "Не удалось получить статистику.",
                cancellationToken: cancellationToken);
            return;
        }

        var text = "📊 Статистика продаж\n\n";
        text += $"📦 Всего заказов: {stats.TotalOrders}\n";
        text += $"⏳ В сборке: {stats.PendingOrders}\n";
        text += $"💳 Ожидают оплату: {stats.AwaitingPaymentOrders}\n";
        text += $"🚚 В пути: {stats.InTransitOrders}\n";
        text += $"✅ Доставлено: {stats.DeliveredOrders}\n";
        text += $"❌ Отменено: {stats.CancelledOrders}\n\n";
        text += $"💰 Общая выручка: {stats.TotalRevenue:N0} ₽\n";
        text += $"💵 Ожидаемая выручка: {stats.PendingRevenue:N0} ₽\n";

        await BotClient.SendTextMessageAsync(
            chatId,
            text,
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("⬅️ Назад", "admin") }
            }),
            cancellationToken: cancellationToken);
    }
}

