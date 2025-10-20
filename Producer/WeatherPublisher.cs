using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

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
      var basicProperties = new BasicProperties
      {
        DeliveryMode = DeliveryModes.Persistent,
        ContentType = "application/json",
        MessageId = Guid.CreateVersion7().ToString(),
        Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        Type = "WeatherForecastBatch",
      };

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

      try
      {
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

        _logger.LogInformation(
          "Published message {MessageId} ({ContentType}) to {RoutingKey}.",
          basicProperties.MessageId,
          basicProperties.ContentType,
          "weather"
        );
      }
      catch (PublishException publishException)
      {
        _logger.LogError(
          publishException,
          "Publish failed for {MessageId} to {RoutingKey}.",
          basicProperties.MessageId,
          "weather"
        );
      }
    }
  }
}
