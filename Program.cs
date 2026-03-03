using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Bebochka.TelegramBot.Services;
using Telegram.Bot;
using Bebochka.TelegramBot.Handlers;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .Build();

var services = new ServiceCollection();

// Add configuration
services.AddSingleton<IConfiguration>(configuration);

// Configure logging
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// Configure HTTP Client for API
var apiBaseUrl = configuration["Api:BaseUrl"] ?? "http://localhost:5000/api";
// Remove /api from BaseUrl to set as BaseAddress, then use /api/ prefix in all requests
var baseUrl = apiBaseUrl.Replace("/api", "").TrimEnd('/');
services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(baseUrl + "/");
});

// Add HttpClientFactory for downloading images
services.AddHttpClient();

// Configure Telegram Bot
var botToken = configuration["TelegramBot:Token"]
    ?? throw new InvalidOperationException("TelegramBot:Token not found in configuration.");

services.AddSingleton<ITelegramBotClient>(provider => new TelegramBotClient(botToken));

// Register services
services.AddSingleton<MessageStorageService>();
services.AddScoped<ApiClient>();
services.AddScoped<MediaService>();

// Register handlers
services.AddScoped<CatalogHandler>();
services.AddScoped<CartHandler>();
services.AddScoped<OrderHandler>();
services.AddScoped<AdminHandler>();

services.AddScoped<TelegramBotService>();

// Build service provider
var serviceProvider = services.BuildServiceProvider();

// Get logger
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("Starting Telegram Bot...");

    var botService = serviceProvider.GetRequiredService<TelegramBotService>();
    await botService.StartAsync();

    logger.LogInformation("Telegram Bot started successfully. Press Ctrl+C to stop.");

    // Keep the application running
    await Task.Delay(Timeout.Infinite);
}
catch (Exception ex)
{
    logger.LogError(ex, "An error occurred while starting the bot");
    throw;
}
finally
{
    serviceProvider.Dispose();
}
