namespace Bebochka.TelegramBot.Models.DTOs;

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public string? Gender { get; set; }
    public string? Condition { get; set; }
    public List<string> Images { get; set; } = new();
    public int QuantityInStock { get; set; }
    public int AvailableQuantity { get; set; }
}

