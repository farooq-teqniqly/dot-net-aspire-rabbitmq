using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Producer
{
  internal sealed class WeatherPublisher : BackgroundService, IWeatherPublisher
  {
    private const string _routingKey = "weather";
    private readonly IChannel _channel;
    private readonly ILogger<WeatherPublisher> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public WeatherPublisher(
      IChannel channel,
      ILogger<WeatherPublisher> logger,
      IServiceScopeFactory serviceScopeFactory
    )
    {
      ArgumentNullException.ThrowIfNull(channel);
      ArgumentNullException.ThrowIfNull(logger);
      ArgumentNullException.ThrowIfNull(serviceScopeFactory);

      _channel = channel;
      _logger = logger;
      _serviceScopeFactory = serviceScopeFactory;

      _channel.BasicReturnAsync += OnBasicReturnAsync;
    }

    public async Task PublishForecastAsync(
      WeatherForecast[] weatherForecast,
      Guid messageId,
      CancellationToken cancellationToken = default
    )
    {
      using (var scope = _serviceScopeFactory.CreateScope())
      {
        var publisherActivity = scope.ServiceProvider.GetRequiredService<PublisherActivity>();

        await publisherActivity
          .PublishAsync(
            messageId,
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

                throw;
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
      }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      while (!stoppingToken.IsCancellationRequested)
      {
        await using (var scope = _serviceScopeFactory.CreateAsyncScope())
        {
          var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
          var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

          await unitOfWork.BeginTransactionAsync(stoppingToken).ConfigureAwait(false);

          await using (unitOfWork)
          {
            var messages = await outboxRepository
              .GetUnprocessedMessagesAsync(2)
              .ConfigureAwait(false);

            foreach (var message in messages)
            {
              try
              {
                var weatherForecast = JsonSerializer.Deserialize<WeatherForecast[]>(
                  message.Content
                );

                if (weatherForecast is null)
                {
                  throw new InvalidOperationException("Failed to deserialize weather forecast.");
                }

                await PublishForecastAsync(weatherForecast, message.Id, stoppingToken)
                  .ConfigureAwait(false);

                await outboxRepository.MarkAsProcessedAsync(message.Id).ConfigureAwait(false);
              }
              catch (InvalidOperationException exception)
              {
                _logger.LogError(exception, "Failed to process outbox message.");

                await outboxRepository
                  .MarkAsErrorAsync(message.Id, exception.Message)
                  .ConfigureAwait(false);
              }
              catch (JsonException exception)
              {
                _logger.LogError(exception, "Failed to process outbox message.");

                await outboxRepository
                  .MarkAsErrorAsync(message.Id, exception.Message)
                  .ConfigureAwait(false);
              }
            }

            await unitOfWork.CommitAsync(stoppingToken).ConfigureAwait(false);
          }
        }

        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ConfigureAwait(false);
      }
    }

    private async Task OnBasicReturnAsync(object? sender, BasicReturnEventArgs args)
    {
      _logger.LogError(
        message: "RETURN {EventReplyCode} {EventReplyText} rk={EventRoutingKey}",
        args.ReplyCode,
        args.ReplyText,
        args.RoutingKey
      );

      await Task.CompletedTask.ConfigureAwait(false);
    }
  }
}
