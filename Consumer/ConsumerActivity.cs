using System.Diagnostics;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client.Events;

namespace Consumer
{
  public sealed class ConsumerActivity
  {
    private static readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;
    private readonly ActivitySource _activitySource;
    private readonly ILogger<ConsumerActivity> _logger;

    public ConsumerActivity(ActivitySource activitySource, ILogger<ConsumerActivity> logger)
    {
      ArgumentNullException.ThrowIfNull(activitySource);
      ArgumentNullException.ThrowIfNull(logger);

      _activitySource = activitySource;
      _logger = logger;
    }

    public async Task<TResult> ConsumeAsync<TResult>(
      string spanName,
      BasicDeliverEventArgs eventArgs,
      Func<Activity?, CancellationToken, Task<TResult>> handleAsync,
      Action<Activity, BasicDeliverEventArgs>? enrich = null,
      CancellationToken cancellationToken = default
    )
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(spanName);
      ArgumentNullException.ThrowIfNull(eventArgs);
      ArgumentNullException.ThrowIfNull(handleAsync);

      var headers = eventArgs.BasicProperties?.Headers;
      var parent = _propagator.Extract(default, headers, ExtractHeader);
      var prevBaggage = Baggage.Current;
      Baggage.Current = parent.Baggage;

      try
      {
        using (
          var activity = _activitySource.StartActivity(
            spanName,
            ActivityKind.Consumer,
            parent.ActivityContext
          )
        )
        {
          activity?.SetTag("messaging.system", "rabbitmq");
          activity?.SetTag("messaging.destination_kind", "queue");
          activity?.SetTag("messaging.destination.name", spanName);
          activity?.SetTag("messaging.operation", "process");

          var messageId = eventArgs.BasicProperties?.MessageId;

          if (!string.IsNullOrEmpty(messageId))
          {
            activity?.SetTag("messaging.message.id", messageId);
          }

          _logger.LogInformation(
            "Received {MessageId} (DeliveryTag={DeliveryTag}) from {Queue}.",
            messageId ?? "no message id",
            eventArgs.DeliveryTag,
            eventArgs.RoutingKey
          );

          if (activity is not null && enrich is not null)
          {
            enrich.Invoke(activity, eventArgs);
          }

          try
          {
            var result = await handleAsync(activity, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
          }
          catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
          {
            activity?.SetStatus(ActivityStatusCode.Unset);
            throw;
          }
          catch (Exception exception)
          {
            activity?.AddException(exception);
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
          }
        }
      }
      finally
      {
        Baggage.Current = prevBaggage;
      }
    }

    private IEnumerable<string>? ExtractHeader(IDictionary<string, object?>? headers, string key)
    {
      if (headers is null || !headers.TryGetValue(key, out var obj) || obj is null)
      {
        return [];
      }

      return obj switch
      {
        byte[] b => new[] { Encoding.UTF8.GetString(b) },
        string s => new[] { s },
        ReadOnlyMemory<byte> rom => new[] { Encoding.UTF8.GetString(rom.Span) },
        _ => Array.Empty<string>(),
      };
    }
  }
}
