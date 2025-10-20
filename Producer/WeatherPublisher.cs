using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Producer
{
  internal sealed class WeatherPublisher : IWeatherPublisher
  {
    private readonly IChannel _channel;
    private readonly ILogger<WeatherPublisher> _logger;

    public WeatherPublisher(IChannel channel, ILogger<WeatherPublisher> logger)
    {
      ArgumentNullException.ThrowIfNull(channel);
      ArgumentNullException.ThrowIfNull(logger);

      _channel = channel;
      _logger = logger;
    }

    public async Task PublishForecastAsync(
      WeatherForecast[] weatherForecast,
      CancellationToken cancellationToken = default
    )
    {
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

      var json = JsonSerializer.Serialize(weatherForecast);
      var payload = Encoding.UTF8.GetBytes(json);

      _logger.LogDebug("Publishing weather forecast to queue.");

      await _channel
        .BasicPublishAsync(
          string.Empty,
          "weather",
          true,
          basicProperties,
          payload,
          cancellationToken
        )
        .ConfigureAwait(false);

      _logger.LogDebug("Published weather forecast to queue.");
    }
  }
}
