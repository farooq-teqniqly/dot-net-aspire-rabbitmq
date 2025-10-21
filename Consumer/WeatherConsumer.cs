using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Consumer
{
  public sealed class WeatherConsumer : BackgroundService
  {
    private readonly IChannel _channel;
    private readonly IServiceScopeFactory _serviceScope;
    private readonly ILogger<WeatherConsumer> _logger;

    public WeatherConsumer(
      IChannel channel,
      ILogger<WeatherConsumer> logger,
      IServiceScopeFactory serviceScope
    )
    {
      ArgumentNullException.ThrowIfNull(channel);
      ArgumentNullException.ThrowIfNull(logger);
      ArgumentNullException.ThrowIfNull(serviceScope);

      _channel = channel;
      _logger = logger;
      _serviceScope = serviceScope;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      if (stoppingToken.IsCancellationRequested)
      {
        return;
      }

      var consumer = new AsyncEventingBasicConsumer(_channel);

      consumer.ReceivedAsync += async (_, args) =>
      {
        try
        {
          if (!stoppingToken.IsCancellationRequested)
          {
            using (var scope = _serviceScope.CreateScope())
            {
              var consumerActivity = scope.ServiceProvider.GetRequiredService<ConsumerActivity>();

              await consumerActivity
                .ConsumeAsync(
                  "weather",
                  args,
                  async (activity, ct) =>
                  {
                    if (args.BasicProperties.ContentType != "application/json")
                    {
                      _logger.LogWarning(
                        "Message has an unprocessable content type of {ContentType}",
                        args.BasicProperties.ContentType
                      );

                      await NackMessage(args, ct).ConfigureAwait(false);
                      return false;
                    }

                    var message = Encoding.UTF8.GetString(args.Body.ToArray());

                    await HandleMessageAsync(message, ct).ConfigureAwait(false);

                    await _channel.BasicAckAsync(args.DeliveryTag, false, ct).ConfigureAwait(false);

                    _logger.LogInformation(
                      "Processed {MessageId} successfully.",
                      args.BasicProperties.MessageId
                    );

                    return true;
                  },
                  (activity, eventArgs) =>
                  {
                    activity.SetTag("messaging.rabbitmq.routing_key", eventArgs.RoutingKey);

                    if (!string.IsNullOrEmpty(eventArgs.BasicProperties.CorrelationId))
                    {
                      activity.SetTag(
                        "messaging.conversation_id",
                        eventArgs.BasicProperties.CorrelationId
                      );
                    }
                  },
                  stoppingToken
                )
                .ConfigureAwait(false);
            }
          }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
          _logger.LogInformation("Message processing cancelled due to service shutdown.");
        }
        catch (JsonException ex)
        {
          _logger.LogError(
            ex,
            "JSON deserialization failed. MessageId={MessageId}",
            args.BasicProperties.MessageId
          );

          await NackMessage(args, stoppingToken).ConfigureAwait(false);
        }
        catch (AlreadyClosedException ex)
        {
          _logger.LogWarning(ex, "Channel closed while processing message.");
        }
        catch (OperationInterruptedException ex)
        {
          _logger.LogError(
            ex,
            "RabbitMQ channel interrupted. Code={Code}, Text={Text}",
            ex.ShutdownReason?.ReplyCode,
            ex.ShutdownReason?.ReplyText
          );
        }
      };

      await _channel
        .BasicConsumeAsync("weather", false, consumer, stoppingToken)
        .ConfigureAwait(false);

      await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    private static async Task HandleMessageAsync(
      string message,
      CancellationToken cancellationToken
    )
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(message);

      await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
    }

    private async Task NackMessage(BasicDeliverEventArgs args, CancellationToken stoppingToken)
    {
      try
      {
        await _channel
          .BasicNackAsync(args.DeliveryTag, false, false, stoppingToken)
          .ConfigureAwait(false);
      }
      catch (InvalidOperationException)
      {
        _logger.LogWarning("Failed to NACK message - channel may be closed.");
      }
    }
  }
}
