namespace Bebochka.TelegramBot.Models.DTOs;

public class OrderDto
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? CustomerAddress { get; set; }
    public string? DeliveryMethod { get; set; }
    public string? Comment { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "В сборке";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public List<OrderItemDto> OrderItems { get; set; } = new();
}

public class OrderItemDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal ProductPrice { get; set; }
    public int Quantity { get; set; }
}

