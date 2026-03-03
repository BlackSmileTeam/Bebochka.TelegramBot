using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Bebochka.TelegramBot.Services;

namespace Bebochka.TelegramBot.Handlers;

/// <summary>
/// Обработчик для работы с каталогом товаров
/// </summary>
public class CatalogHandler : BaseHandler
{
    private readonly MediaService _mediaService;

    public CatalogHandler(
        ITelegramBotClient botClient,
        ApiClient apiClient,
        ILogger<CatalogHandler> logger,
        IConfiguration configuration,
        MessageStorageService messageStorage,
        MediaService mediaService)
        : base(botClient, apiClient, logger, configuration, messageStorage)
    {
        _mediaService = mediaService;
    }

    public async Task ShowCatalogAsync(long chatId, CancellationToken cancellationToken)
    {
        // Удаляем предыдущие сообщения каталога
        var oldMessages = MessageStorage.GetCatalogMessages(chatId);
        foreach (var msgId in oldMessages)
        {
            try
            {
                await BotClient.DeleteMessageAsync(chatId, msgId, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"Failed to delete catalog message {msgId}");
            }
        }

        MessageStorage.ClearCatalogMessages(chatId);

        var sessionId = chatId.ToString();
        var products = await ApiClient.GetProductsAsync(sessionId);

        if (!products.Any())
        {
            var emptyMsg = await BotClient.SendTextMessageAsync(
                chatId,
                "Каталог пуст.",
                cancellationToken: cancellationToken);
            MessageStorage.AddCatalogMessage(chatId, emptyMsg.MessageId);
            return;
        }

        // Отправляем заголовок каталога
        var headerMsg = await BotClient.SendTextMessageAsync(
            chatId,
            "📦 Каталог товаров:",
            cancellationToken: cancellationToken);
        MessageStorage.AddCatalogMessage(chatId, headerMsg.MessageId);

        // Отправляем каждый товар
        var availableProducts = products.Where(p => p.AvailableQuantity > 0).Take(20).ToList();

        foreach (var product in availableProducts)
        {
            try
            {
                await SendProductAsync(chatId, product, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error sending product {product.Id} to chat {chatId}");
            }
        }

        // Отправляем навигационные кнопки
        var navKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🛒 Корзина", "cart"),
                InlineKeyboardButton.WithCallbackData("📋 Заказы", "orders")
            }
        });

        var navMsg = await BotClient.SendTextMessageAsync(
            chatId,
            "Используйте кнопки для навигации:",
            replyMarkup: navKeyboard,
            cancellationToken: cancellationToken);

        MessageStorage.AddCatalogMessage(chatId, navMsg.MessageId);
    }

    public async Task ShowProductDetailsAsync(long chatId, int productId, CancellationToken cancellationToken)
    {
        var sessionId = chatId.ToString();
        var product = await ApiClient.GetProductByIdAsync(productId, sessionId);

        if (product == null || product.AvailableQuantity <= 0)
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                "Товар не найден или закончился.",
                cancellationToken: cancellationToken);
            return;
        }

        var text = $"📦 {product.Name}\n\n";
        if (!string.IsNullOrEmpty(product.Brand))
            text += $"🏷️ Бренд: {product.Brand}\n";
        if (!string.IsNullOrEmpty(product.Description))
            text += $"📝 {product.Description}\n\n";
        if (!string.IsNullOrEmpty(product.Size))
            text += $"📏 Размер: {product.Size}\n";
        if (!string.IsNullOrEmpty(product.Color))
            text += $"🎨 Цвет: {product.Color}\n";
        if (!string.IsNullOrEmpty(product.Gender))
            text += $"👤 Пол: {product.Gender}\n";
        if (!string.IsNullOrEmpty(product.Condition))
            text += $"✨ Состояние: {product.Condition}\n";
        text += $"\n💰 Цена: {product.Price:N0} ₽\n";
        text += $"✅ В наличии: {product.AvailableQuantity} шт.\n";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("➕ В корзину", $"addtocart:{product.Id}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🛒 Корзина", "cart"),
                InlineKeyboardButton.WithCallbackData("📋 Каталог", "catalog")
            }
        });

        var imageUrls = GetImageUrls(product);
        if (imageUrls.Count > 0)
        {
            try
            {
                var detailMessageIds = await _mediaService.SendProductPhotosAsync(
                    chatId, imageUrls, text, keyboard, cancellationToken);
                MessageStorage.SetProductMessages(chatId, product.Id, detailMessageIds);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"Failed to send photos for product details {product.Id}");
                await BotClient.SendTextMessageAsync(
                    chatId,
                    text,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
            }
        }
        else
        {
            await BotClient.SendTextMessageAsync(
                chatId,
                text,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
    }

    private async Task SendProductAsync(long chatId, Models.DTOs.ProductDto product, CancellationToken cancellationToken)
    {
        var caption = $"🛍️ {product.Name}\n";
        if (!string.IsNullOrEmpty(product.Brand))
            caption += $"🏷️ Бренд: {product.Brand}\n";
        if (!string.IsNullOrEmpty(product.Size))
            caption += $"📏 Размер: {product.Size}\n";
        if (!string.IsNullOrEmpty(product.Color))
            caption += $"🎨 Цвет: {product.Color}\n";
        if (!string.IsNullOrEmpty(product.Gender))
            caption += $"👤 Пол: {product.Gender}\n";
        if (!string.IsNullOrEmpty(product.Condition))
            caption += $"✨ Состояние: {product.Condition}\n";
        caption += $"\n💰 Цена: {product.Price:N0} ₽\n";
        caption += $"✅ В наличии: {product.AvailableQuantity} шт.\n";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("➕ В корзину", $"addtocart:{product.Id}")
            }
        });

        var imageUrls = GetImageUrls(product);
        if (imageUrls.Count > 0)
        {
            try
            {
                var productMessageIds = await _mediaService.SendProductPhotosAsync(
                    chatId, imageUrls, caption, keyboard, cancellationToken);
                MessageStorage.SetProductMessages(chatId, product.Id, productMessageIds);
                MessageStorage.AddCatalogMessages(chatId, productMessageIds);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"Failed to send photos for product {product.Id}");
                var textMsg = await BotClient.SendTextMessageAsync(
                    chatId,
                    caption,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
                MessageStorage.SetProductMessages(chatId, product.Id, new List<int> { textMsg.MessageId });
                MessageStorage.AddCatalogMessage(chatId, textMsg.MessageId);
            }
        }
        else
        {
            var textMsg = await BotClient.SendTextMessageAsync(
                chatId,
                caption,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
            MessageStorage.SetProductMessages(chatId, product.Id, new List<int> { textMsg.MessageId });
            MessageStorage.AddCatalogMessage(chatId, textMsg.MessageId);
        }
    }

    private List<string> GetImageUrls(Models.DTOs.ProductDto product)
    {
        var imageUrls = new List<string>();
        if (product.Images != null && product.Images.Count > 0)
        {
            var apiBaseUrl = Configuration["Api:BaseUrl"]?.Replace("/api", "").TrimEnd('/') ?? "http://89.104.67.36:55501";

            foreach (var imagePath in product.Images)
            {
                string? fullUrl = null;
                if (imagePath.StartsWith("http"))
                {
                    fullUrl = imagePath;
                }
                else if (imagePath.StartsWith("/"))
                {
                    fullUrl = $"{apiBaseUrl}{imagePath}";
                }
                else
                {
                    fullUrl = $"{apiBaseUrl}/{imagePath.TrimStart('/')}";
                }

                if (!string.IsNullOrEmpty(fullUrl))
                {
                    imageUrls.Add(fullUrl);
                }
            }
        }
        return imageUrls;
    }
}

