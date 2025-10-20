using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

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
      _logger.LogDebug($"Started {nameof(ExecuteAsync)}.");

      if (stoppingToken.IsCancellationRequested)
      {
        return;
      }

      var consumer = new AsyncEventingBasicConsumer(_channel);

      consumer.ReceivedAsync += async (_, args) =>
      {
        try
        {
          var message = Encoding.UTF8.GetString(args.Body.ToArray());

          if (!stoppingToken.IsCancellationRequested)
          {
            await HandleMessageAsync(message, stoppingToken).ConfigureAwait(false);

            await _channel
              .BasicAckAsync(args.DeliveryTag, false, stoppingToken)
              .ConfigureAwait(false);
          }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
          _logger.LogInformation("Message processing cancelled due to service shutdown.");
        }
        catch (InvalidOperationException invalidOpException)
        {
          _logger.LogError(
            invalidOpException,
            "Invalid operation during message processing (likely channel closed)."
          );
        }
        catch (ArgumentException argException)
        {
          _logger.LogError(argException, "Invalid argument during message processing.");

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
        catch (Exception exception)
        {
          _logger.LogError(exception, "Unexpected error during message processing.");
          throw;
        }
      };

      await _channel
        .BasicConsumeAsync("weather", false, consumer, stoppingToken)
        .ConfigureAwait(false);

      await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);

      _logger.LogDebug($"Finished {nameof(ExecuteAsync)}.");
    }

    private Task HandleMessageAsync(string message, CancellationToken cancellationToken)
    {
      _logger.LogDebug("Message received: {Message}", message);
      return Task.CompletedTask;
    }
  }
}
