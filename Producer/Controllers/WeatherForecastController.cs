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

  private readonly ILogger<WeatherForecastController> _logger;
  private readonly IOutboxRepository _outboxRepository;
  private readonly IUnitOfWork _unitOfWork;

  public WeatherForecastController(
    IUnitOfWork unitOfWork,
    IOutboxRepository outboxRepository,
    ILogger<WeatherForecastController> logger
  )
  {
    ArgumentNullException.ThrowIfNull(unitOfWork);
    ArgumentNullException.ThrowIfNull(outboxRepository);
    ArgumentNullException.ThrowIfNull(logger);

    _unitOfWork = unitOfWork;
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
      await _unitOfWork.BeginTransactionAsync().ConfigureAwait(false);

      await using (_unitOfWork)
      {
        await _outboxRepository.AddToOutboxAsync(forecast).ConfigureAwait(false);
        await _unitOfWork.CommitAsync().ConfigureAwait(false);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to add weather forecast to outbox");
      return StatusCode(500, "Failed to process request");
    }

    return Ok(forecast);
  }
}
