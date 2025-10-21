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

  private readonly IOutboxRepository _outboxRepository;
  private readonly ILogger<WeatherForecastController> _logger;

  public WeatherForecastController(
    IOutboxRepository outboxRepository,
    ILogger<WeatherForecastController> logger
  )
  {
    ArgumentNullException.ThrowIfNull(outboxRepository);
    ArgumentNullException.ThrowIfNull(logger);

    _outboxRepository = outboxRepository;
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

    try
    {
        await _outboxRepository.AddToOutboxAsync(forecast).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to add weather forecast to outbox");
        return StatusCode(500, "Failed to process request");
    }

    return Ok(forecast);
  }
}
