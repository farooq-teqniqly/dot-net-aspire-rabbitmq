using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Consumer
{
  public sealed class WeatherConsumer : BackgroundService
  {
    private readonly IChannel _channel;
    private readonly ILogger<WeatherConsumer> _logger;

    public WeatherConsumer(IChannel channel, ILogger<WeatherConsumer> logger)
    {
      ArgumentNullException.ThrowIfNull(channel);
      ArgumentNullException.ThrowIfNull(logger);

      _channel = channel;
      _logger = logger;
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
            _logger.LogInformation(
              "Received {MessageId} (DeliveryTag={DeliveryTag}) from {Queue}.",
              args.BasicProperties.MessageId,
              args.DeliveryTag,
              "weather"
            );

            if (args.BasicProperties.ContentType != "application/json")
            {
              _logger.LogWarning(
                "Message has an unprocessable content type of {ContentType}",
                args.BasicProperties.ContentType
              );

              await NackMessage(args, stoppingToken).ConfigureAwait(false);
              return;
            }

            var message = Encoding.UTF8.GetString(args.Body.ToArray());

            await HandleMessageAsync(message, stoppingToken).ConfigureAwait(false);

            await _channel
              .BasicAckAsync(args.DeliveryTag, false, stoppingToken)
              .ConfigureAwait(false);

            _logger.LogInformation(
              "Processed {MessageId} successfully.",
              args.BasicProperties.MessageId
            );
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

    private static async Task HandleMessageAsync(
      string message,
      CancellationToken cancellationToken
    ) => await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
  }
}
