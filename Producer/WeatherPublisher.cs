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
    private readonly PublisherActivity _publisherActivity;
    private const string _routingKey = "weather";

    public WeatherPublisher(
      IChannel channel,
      ILogger<WeatherPublisher> logger,
      PublisherActivity publisherActivity
    )
    {
      ArgumentNullException.ThrowIfNull(channel);
      ArgumentNullException.ThrowIfNull(logger);
      ArgumentNullException.ThrowIfNull(publisherActivity);

      _channel = channel;
      _logger = logger;
      _publisherActivity = publisherActivity;
    }

    public async Task PublishForecastAsync(
      WeatherForecast[] weatherForecast,
      CancellationToken cancellationToken = default
    )
    {
      await _publisherActivity
        .PublishAsync(
          "rabbitmq.publish",
          sendAsync: async (basicProperties, ct) =>
          {
            basicProperties.ContentType = "application/json";
            basicProperties.DeliveryMode = DeliveryModes.Persistent;

            var json = JsonSerializer.Serialize(weatherForecast);
            var payload = Encoding.UTF8.GetBytes(json);

            try
            {
              await _channel
                .BasicPublishAsync(string.Empty, _routingKey, true, basicProperties, payload, ct)
                .ConfigureAwait(false);

              _logger.LogInformation(
                "Published message {MessageId} ({ContentType}) to {RoutingKey}.",
                basicProperties.MessageId,
                basicProperties.ContentType,
                _routingKey
              );

              return true;
            }
            catch (PublishException publishException)
            {
              _logger.LogError(
                publishException,
                "Publish failed for {MessageId} to {RoutingKey}.",
                basicProperties.MessageId,
                _routingKey
              );

              return false;
            }
          },
          enrich: activity =>
          {
            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.destination_kind", "queue");
            activity.SetTag("messaging.destination.name", _routingKey);
            activity.SetTag("messaging.operation", "publish");
          },
          cancellationToken
        )
        .ConfigureAwait(false);

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
    }
  }
}
