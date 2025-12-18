namespace Bebochka.TelegramBot.Services;

/// <summary>
/// Сервис для хранения связи между сообщениями Telegram и товарами/каталогом
/// </summary>
public class MessageStorageService
{
    // Хранение сообщений каталога для удаления
    private readonly Dictionary<long, List<int>> _catalogMessages = new();
    
    // Хранение связи productId -> список messageId (для удаления при добавлении в корзину)
    private readonly Dictionary<string, List<int>> _productMessages = new();
    
    private readonly object _lock = new();

    public void AddCatalogMessage(long chatId, int messageId)
    {
        lock (_lock)
        {
            if (!_catalogMessages.TryGetValue(chatId, out var messages))
            {
                messages = new List<int>();
                _catalogMessages[chatId] = messages;
            }
            messages.Add(messageId);
        }
    }

    public void AddCatalogMessages(long chatId, IEnumerable<int> messageIds)
    {
        lock (_lock)
        {
            if (!_catalogMessages.TryGetValue(chatId, out var messages))
            {
                messages = new List<int>();
                _catalogMessages[chatId] = messages;
            }
            messages.AddRange(messageIds);
        }
    }

    public List<int> GetCatalogMessages(long chatId)
    {
        lock (_lock)
        {
            return _catalogMessages.TryGetValue(chatId, out var messages) 
                ? new List<int>(messages) 
                : new List<int>();
        }
    }

    public void ClearCatalogMessages(long chatId)
    {
        lock (_lock)
        {
            if (_catalogMessages.TryGetValue(chatId, out var messages))
            {
                messages.Clear();
            }
        }
    }

    public void RemoveCatalogMessages(long chatId, IEnumerable<int> messageIds)
    {
        lock (_lock)
        {
            if (_catalogMessages.TryGetValue(chatId, out var messages))
            {
                messages.RemoveAll(id => messageIds.Contains(id));
            }
        }
    }

    public void SetProductMessages(long chatId, int productId, List<int> messageIds)
    {
        lock (_lock)
        {
            var key = $"{chatId}_{productId}";
            _productMessages[key] = new List<int>(messageIds);
        }
    }

    public List<int>? GetProductMessages(long chatId, int productId)
    {
        lock (_lock)
        {
            var key = $"{chatId}_{productId}";
            return _productMessages.TryGetValue(key, out var messages) 
                ? new List<int>(messages) 
                : null;
        }
    }

    public void RemoveProductMessages(long chatId, int productId)
    {
        lock (_lock)
        {
            var key = $"{chatId}_{productId}";
            _productMessages.Remove(key);
        }
    }
}

