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
    private PublisherConfirmationTracker _confirmationTracker = null!;

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

      _channel.BasicAcksAsync += OnBasicAcksAsync;
      _channel.BasicNacksAsync += OnBasicNacksAsync;
      _channel.BasicReturnAsync += OnBasicReturnAsync;
    }

    public async Task PublishForecastAsync(
      WeatherForecast[] weatherForecast,
      Guid messageId,
      CancellationToken cancellationToken = default
    )
    {
      await using (var scope = _serviceScopeFactory.CreateAsyncScope())
      {
        var publisherActivity = scope.ServiceProvider.GetRequiredService<PublisherActivity>();

        _confirmationTracker =
          scope.ServiceProvider.GetRequiredService<PublisherConfirmationTracker>();

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
                // Get the next sequence number before publishing
                var sequenceNumber = await _channel
                  .GetNextPublishSequenceNumberAsync(ct)
                  .ConfigureAwait(false);

                _logger.LogDebug(
                  "Publishing message {MessageId} with sequence number {SequenceNumber}",
                  basicProperties.MessageId,
                  sequenceNumber
                );

                // Start tracking the confirmation
                var confirmationTask = _confirmationTracker.TrackPublishAsync(sequenceNumber);

                // Publish the message
                await _channel
                  .BasicPublishAsync(string.Empty, _routingKey, true, basicProperties, payload, ct)
                  .ConfigureAwait(false);

                _logger.LogInformation(
                  "Published message {MessageId} ({ContentType}) to {RoutingKey}, awaiting confirmation...",
                  basicProperties.MessageId,
                  basicProperties.ContentType,
                  _routingKey
                );

                // Wait for confirmation with timeout
                var confirmed = await confirmationTask
                  .WaitAsync(TimeSpan.FromSeconds(30), ct)
                  .ConfigureAwait(false);

                if (!confirmed)
                {
                  _logger.LogError(
                    "Message {MessageId} with sequence {SequenceNumber} was NACKed by broker",
                    basicProperties.MessageId,
                    sequenceNumber
                  );

                  throw new InvalidOperationException(
                    $"Message {basicProperties.MessageId} was not confirmed by broker (NACK received)"
                  );
                }

                _logger.LogInformation(
                  "Message {MessageId} with sequence {SequenceNumber} confirmed by broker",
                  basicProperties.MessageId,
                  sequenceNumber
                );

                return true;
              }
              catch (TimeoutException timeoutException)
              {
                _logger.LogError(
                  timeoutException,
                  "Timeout waiting for confirmation for message {MessageId}",
                  basicProperties.MessageId
                );

                throw new InvalidOperationException(
                  $"Timeout waiting for confirmation for message {basicProperties.MessageId}",
                  timeoutException
                );
              }
              catch (Exception ex)
              {
                _logger.LogError(
                  ex,
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
                var weatherForecast =
                  JsonSerializer.Deserialize<WeatherForecast[]>(message.Content)
                  ?? throw new InvalidOperationException("Failed to deserialize weather forecast.");

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

    private async Task OnBasicAcksAsync(object? sender, BasicAckEventArgs args)
    {
      _confirmationTracker.HandleAck(args.DeliveryTag, args.Multiple);
      await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task OnBasicNacksAsync(object? sender, BasicNackEventArgs args)
    {
      _confirmationTracker.HandleNack(args.DeliveryTag, args.Multiple);
      await Task.CompletedTask.ConfigureAwait(false);
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
