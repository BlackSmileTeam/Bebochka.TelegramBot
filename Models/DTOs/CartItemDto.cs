namespace Bebochka.TelegramBot.Models.DTOs;

public class CartItemDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductBrand { get; set; }
    public string? ProductSize { get; set; }
    public string? ProductColor { get; set; }
    public List<string> ProductImages { get; set; } = new();
    public decimal ProductPrice { get; set; }
    public int Quantity { get; set; }
}

