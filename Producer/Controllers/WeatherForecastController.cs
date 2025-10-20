using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;

namespace Producer.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
  private static readonly string[] _summaries =
  [
    "Freezing",
    "Bracing",
    "Chilly",
    "Cool",
    "Mild",
    "Warm",
    "Balmy",
    "Hot",
    "Sweltering",
    "Scorching",
  ];

  private readonly IChannel _channel;
  private readonly ILogger<WeatherForecastController> _logger;

  public WeatherForecastController(IChannel channel, ILogger<WeatherForecastController> logger)
  {
    ArgumentNullException.ThrowIfNull(channel);
    ArgumentNullException.ThrowIfNull(logger);

    _channel = channel;
    _logger = logger;
  }

  [HttpGet(Name = "GetWeatherForecast")]
  public async Task<ActionResult<WeatherForecast>> Get()
  {
    var forecast = Enumerable
      .Range(1, 5)
      .Select(index => new WeatherForecast
      {
        Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
        TemperatureC = RandomNumberGenerator.GetInt32(-20, 55),
        Summary = _summaries[RandomNumberGenerator.GetInt32(_summaries.Length)],
      })
      .ToArray();

    var basicProperties = new BasicProperties { DeliveryMode = DeliveryModes.Persistent };

    _channel.BasicReturnAsync += async (_, args) =>
    {
      _logger.LogError(
        message: "RETURN {EventReplyCode} {EventReplyText} rk={EventRoutingKey}",
        args.ReplyCode,
        args.ReplyText,
        args.RoutingKey
      );

      await Task.CompletedTask.ConfigureAwait(false);
    };

    var json = JsonSerializer.Serialize(forecast);
    var payload = Encoding.UTF8.GetBytes(json);

    _logger.LogDebug("Publishing weather forecast to queue.");

    await _channel
      .BasicPublishAsync(
        string.Empty,
        "weather",
        true,
        basicProperties,
        payload,
        HttpContext.RequestAborted
      )
      .ConfigureAwait(false);

    _logger.LogDebug("Published weather forecast to queue.");

    return Ok(forecast);
  }
}
