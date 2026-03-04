using System.Net.Http.Json;
using Bebochka.TelegramBot.Models.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bebochka.TelegramBot.Services;

public class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiClient> _logger;
    private readonly string _baseUrl;

    public ApiClient(HttpClient httpClient, ILogger<ApiClient> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration["Api:BaseUrl"] ?? "http://localhost:5000/api";
        
        // BaseAddress should be set in Program.cs via AddHttpClient
        // If not set, set it here as fallback
        if (_httpClient.BaseAddress == null)
        {
            var baseUrl = _baseUrl.Replace("/api", "").TrimEnd('/');
            _httpClient.BaseAddress = new Uri(baseUrl + "/");
        }
        
        _logger.LogInformation($"ApiClient initialized with BaseUrl: {_httpClient.BaseAddress}");
    }

    public async Task<List<ProductDto>> GetProductsAsync(string? sessionId = null)
    {
        try
        {
            var url = sessionId != null 
                ? $"/api/Products?sessionId={Uri.EscapeDataString(sessionId)}" 
                : "/api/Products";
            
            var fullUrl = new Uri(_httpClient.BaseAddress!, url).ToString();
            _logger.LogInformation($"Fetching products from: {fullUrl}");
            
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get products. Status: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Error content: {errorContent}");
                return new List<ProductDto>();
            }
            
            var products = await response.Content.ReadFromJsonAsync<List<ProductDto>>();
            _logger.LogInformation($"Successfully fetched {products?.Count ?? 0} products");
            
            // Логируем информацию о изображениях для первых нескольких продуктов
            if (products != null && products.Any())
            {
                foreach (var product in products.Take(3))
                {
                    _logger.LogInformation($"Product {product.Id} ({product.Name}): {product.Images?.Count ?? 0} images. First image: {(product.Images?.FirstOrDefault() ?? "none")}");
                }
            }
            
            return products ?? new List<ProductDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting products");
            return new List<ProductDto>();
        }
    }

    public async Task<ProductDto?> GetProductByIdAsync(int productId, string? sessionId = null)
    {
        try
        {
            var url = sessionId != null 
                ? $"/api/Products/{productId}?sessionId={Uri.EscapeDataString(sessionId)}" 
                : $"/api/Products/{productId}";
            
            var response = await _httpClient.GetAsync(url);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ProductDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product {ProductId}", productId);
            return null;
        }
    }

    public async Task<List<CartItemDto>> GetCartItemsAsync(string sessionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/Cart?sessionId={Uri.EscapeDataString(sessionId)}");
            response.EnsureSuccessStatusCode();
            
            var items = await response.Content.ReadFromJsonAsync<List<CartItemDto>>();
            return items ?? new List<CartItemDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart items");
            return new List<CartItemDto>();
        }
    }

    public async Task<bool> AddToCartAsync(string sessionId, int productId, int quantity = 1)
    {
        try
        {
            var request = new
            {
                sessionId,
                productId,
                quantity
            };

            var response = await _httpClient.PostAsJsonAsync("/api/Cart", request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding to cart");
            return false;
        }
    }

    public async Task<bool> UpdateCartItemAsync(int cartItemId, int quantity)
    {
        try
        {
            var request = new
            {
                quantity
            };

            var response = await _httpClient.PutAsJsonAsync($"/api/Cart/{cartItemId}", request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating cart item");
            return false;
        }
    }

    public async Task<bool> RemoveFromCartAsync(int cartItemId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/Cart/{cartItemId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing from cart");
            return false;
        }
    }

    public async Task<bool> ClearCartAsync(string sessionId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/Cart?sessionId={Uri.EscapeDataString(sessionId)}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cart");
            return false;
        }
    }

    public async Task<OrderDto?> CreateOrderAsync(CreateOrderDto orderDto)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/Orders", orderDto);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<OrderDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order");
            return null;
        }
    }

    public async Task<List<OrderDto>> GetUserOrdersAsync(int userId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/Orders/user?userId={userId}");
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // 404 означает что заказов нет - это нормально, возвращаем пустой список
                return new List<OrderDto>();
            }
            
            response.EnsureSuccessStatusCode();
            
            var orders = await response.Content.ReadFromJsonAsync<List<OrderDto>>();
            return orders ?? new List<OrderDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user orders");
            return new List<OrderDto>();
        }
    }

    public async Task<OrderDto?> GetOrderByIdAsync(int orderId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/Orders/{orderId}/public");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OrderDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting order {OrderId}", orderId);
            return null;
        }
    }

    public async Task<bool> CancelOrderAsync(int orderId, string? reason = null)
    {
        try
        {
            var request = new
            {
                reason
            };

            var response = await _httpClient.PostAsJsonAsync($"/api/Orders/{orderId}/cancel", request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling order");
            return false;
        }
    }

    // Admin methods
    public async Task<List<OrderDto>> GetAllOrdersAsync(string? status = null)
    {
        try
        {
            var url = status != null 
                ? $"/api/Orders?status={Uri.EscapeDataString(status)}" 
                : "/api/Orders";
            
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var orders = await response.Content.ReadFromJsonAsync<List<OrderDto>>();
            return orders ?? new List<OrderDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all orders");
            return new List<OrderDto>();
        }
    }

    public async Task<bool> UpdateOrderStatusAsync(int orderId, string status)
    {
        try
        {
            var request = new
            {
                status
            };

            var response = await _httpClient.PostAsJsonAsync($"/api/Orders/{orderId}/status", request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating order status");
            return false;
        }
    }

    public async Task<StatisticsDto?> GetStatisticsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/Orders/statistics");
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<StatisticsDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting statistics");
            return null;
        }
    }

    public async Task<bool> IsAdminAsync(long telegramUserId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/Users/isadmin/{telegramUserId}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;
            
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<bool>();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking admin status for user {telegramUserId}");
            return false;
        }
    }

    /// <summary>
    /// Registers a Telegram User ID for notifications (called automatically when user interacts with bot)
    /// </summary>
    /// <param name="telegramUserId">Telegram User ID</param>
    /// <returns>True if registered successfully</returns>
    public async Task<bool> RegisterTelegramUserAsync(long telegramUserId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/Telegram/register/{telegramUserId}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error registering Telegram user {telegramUserId}");
            return false;
        }
    }

    /// <summary>
    /// Reserves a product from channel post (first comment with "мне"/"я"/"беру"/"бронь").
    /// </summary>
    public async Task<ReserveFromTelegramResultDto?> ReserveFromTelegramAsync(string channelId, int messageId, long telegramUserId, string? username, string? firstName, string? lastName, string? customerPhone = null)
    {
        try
        {
            var body = new
            {
                ChannelId = channelId,
                MessageId = messageId,
                TelegramUserId = telegramUserId,
                Username = username,
                FirstName = firstName,
                LastName = lastName,
                CustomerPhone = customerPhone
            };
            var response = await _httpClient.PostAsJsonAsync("/api/Orders/reserve-from-telegram", body);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ReserveFromTelegramResultDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling reserve-from-telegram");
            return null;
        }
    }
}

public class ReserveFromTelegramResultDto
{
    public bool Success { get; set; }
    public OrderDto? Order { get; set; }
    public string? Reason { get; set; }
}

public class StatisticsDto
{
    public int TotalOrders { get; set; }
    public int PendingOrders { get; set; }
    public int AwaitingPaymentOrders { get; set; }
    public int InTransitOrders { get; set; }
    public int DeliveredOrders { get; set; }
    public int CancelledOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal PendingRevenue { get; set; }
}

