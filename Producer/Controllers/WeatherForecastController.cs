using System.Security.Cryptography;
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

  private readonly IConnection _rabbitMqConnection;
  private readonly ILogger<WeatherForecastController> _logger;

  public WeatherForecastController(
    IConnection rabbitMqConnection,
    ILogger<WeatherForecastController> logger
  )
  {
    ArgumentNullException.ThrowIfNull(rabbitMqConnection);
    ArgumentNullException.ThrowIfNull(logger);

    _rabbitMqConnection = rabbitMqConnection;
    _logger = logger;
  }

  [HttpGet(Name = "GetWeatherForecast")]
  public IEnumerable<WeatherForecast> Get()
  {
    return Enumerable
      .Range(1, 5)
      .Select(index => new WeatherForecast
      {
        Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
        TemperatureC = RandomNumberGenerator.GetInt32(-20, 55),
        Summary = _summaries[RandomNumberGenerator.GetInt32(_summaries.Length)],
      })
      .ToArray();
  }
}
