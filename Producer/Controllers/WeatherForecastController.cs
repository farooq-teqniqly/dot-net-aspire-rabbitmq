using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;

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

  private readonly IWeatherPublisher _weatherPublisher;
  private readonly ILogger<WeatherForecastController> _logger;

  public WeatherForecastController(
    IWeatherPublisher weatherPublisher,
    ILogger<WeatherForecastController> logger
  )
  {
    ArgumentNullException.ThrowIfNull(weatherPublisher);
    ArgumentNullException.ThrowIfNull(logger);

    _weatherPublisher = weatherPublisher;
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

    using (var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
    {
      await _weatherPublisher
        .PublishForecastAsync(forecast, cancellationTokenSource.Token)
        .ConfigureAwait(false);
    }

    return Ok(forecast);
  }
}
