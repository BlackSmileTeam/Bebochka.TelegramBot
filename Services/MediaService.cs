using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Net.Http;
using System.IO;

namespace Bebochka.TelegramBot.Services;

/// <summary>
/// Сервис для работы с медиа-группами и отправки фотографий товаров
/// </summary>
public class MediaService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MediaService> _logger;

    public MediaService(
        ITelegramBotClient botClient,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<MediaService> logger)
    {
        _botClient = botClient;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<int>> SendProductPhotosAsync(
        long chatId,
        List<string> imageUrls,
        string caption,
        InlineKeyboardMarkup? keyboard,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation($"SendProductPhotosAsync called: chatId={chatId}, images={imageUrls.Count}, keyboard is null={keyboard == null}");

        var httpClient = _httpClientFactory.CreateClient();

        try
        {
            _logger.LogInformation($"Downloading {imageUrls.Count} images in parallel");

            // Скачиваем все изображения параллельно
            var downloadTasks = imageUrls.Select(async (url, index) =>
            {
                try
                {
                    _logger.LogInformation($"Downloading image {index + 1}/{imageUrls.Count} from: {url}");
                    var imageBytes = await httpClient.GetByteArrayAsync(url);
                    var extension = GetFileExtension(url);
                    _logger.LogInformation($"Downloaded image {index + 1}: {imageBytes.Length} bytes, extension: {extension}");
                    return new { Bytes = imageBytes, Extension = extension, Index = index, Url = url };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to download image {index + 1} from {url}");
                    return null;
                }
            }).ToList();

            var images = (await Task.WhenAll(downloadTasks))
                .Where(img => img != null)
                .OrderBy(img => img.Index)
                .ToList();

            if (images.Count == 0)
            {
                throw new Exception("Failed to download any images");
            }

            _logger.LogInformation($"Successfully downloaded {images.Count} images");

            // Отправляем все фото одной медиа-группой
            var mediaGroup = new List<IAlbumInputMedia>();

            for (int i = 0; i < images.Count; i++)
            {
                var image = images[i];
                _logger.LogInformation($"Adding image {i + 1}/{images.Count} to media group: {image.Bytes.Length} bytes, URL: {image.Url}");

                var imageBytesCopy = new byte[image.Bytes.Length];
                Buffer.BlockCopy(image.Bytes, 0, imageBytesCopy, 0, image.Bytes.Length);

                var imageStream = new MemoryStream(imageBytesCopy, writable: false);
                var inputFile = InputFile.FromStream(
                    imageStream,
                    $"image{i}{image.Extension}");

                if (i == images.Count - 1)
                {
                    // Последнее фото с описанием товара
                    mediaGroup.Add(new InputMediaPhoto(inputFile)
                    {
                        Caption = caption,
                        ParseMode = ParseMode.Markdown
                    });
                }
                else
                {
                    // Остальные фото без подписи
                    mediaGroup.Add(new InputMediaPhoto(inputFile));
                }
            }

            // Отправляем медиа-группу
            _logger.LogInformation($"Sending media group with {mediaGroup.Count} photos (caption on last photo)");
            var sentMessages = await _botClient.SendMediaGroupAsync(
                chatId: chatId,
                media: mediaGroup,
                cancellationToken: cancellationToken);

            _logger.LogInformation($"Media group sent successfully, received {sentMessages?.Length ?? 0} messages");

            var messageIds = new List<int>();
            if (sentMessages != null)
            {
                messageIds.AddRange(sentMessages.Select(m => m.MessageId));
            }

            // Отправляем кнопки отдельным сообщением
            if (keyboard != null)
            {
                _logger.LogInformation("Sending buttons as separate message");
                try
                {
                    var buttonMsg = await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Для добавления товара в корзину нажмите кнопку ниже:",
                        replyMarkup: keyboard,
                        cancellationToken: cancellationToken);

                    messageIds.Add(buttonMsg.MessageId);
                    _logger.LogInformation($"✅ Successfully sent buttons as separate message");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ Failed to send buttons: {ex.Message}");
                }
            }

            _logger.LogInformation($"Successfully sent {images.Count} photos with buttons to chat {chatId}");
            return messageIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error downloading or sending photos");
            throw;
        }
    }

    private string GetFileExtension(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            if (path.Contains('.'))
            {
                var extension = Path.GetExtension(path);
                if (!string.IsNullOrEmpty(extension))
                    return extension;
            }
        }
        catch
        {
            // Игнорируем ошибки парсинга URL
        }
        return ".jpg"; // По умолчанию
    }
}

