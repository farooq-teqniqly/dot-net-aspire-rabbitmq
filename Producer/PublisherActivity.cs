using System.Diagnostics;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace Producer
{
  internal sealed class PublisherActivity
  {
    private static readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;
    private readonly ActivitySource _activitySource;
    private readonly ILogger<PublisherActivity> _logger;

    public PublisherActivity(ActivitySource activitySource, ILogger<PublisherActivity> logger)
    {
      ArgumentNullException.ThrowIfNull(activitySource);
      ArgumentNullException.ThrowIfNull(logger);

      _activitySource = activitySource;
      _logger = logger;
    }

    public async Task<TResult> PublishAsync<TResult>(
      Guid messageId,
      string spanName,
      Func<BasicProperties, CancellationToken, Task<TResult>> sendAsync,
      Action<Activity>? enrich = null,
      CancellationToken cancellationToken = default
    )
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(spanName);
      ArgumentNullException.ThrowIfNull(sendAsync);

      using (var activity = _activitySource.StartActivity(spanName, ActivityKind.Producer))
      {
        var basicProperties = new BasicProperties { MessageId = messageId.ToString() };

        activity?.SetTag("messaging.message.id", basicProperties.MessageId);

        basicProperties.Headers ??= new Dictionary<string, object?>();

        var traceContext = new PropagationContext(
          Activity.Current?.Context ?? default,
          Baggage.Current
        );

        _propagator.Inject(traceContext, basicProperties.Headers, InjectHeader);

        if (activity is not null && enrich is not null)
        {
          enrich.Invoke(activity);
        }

        try
        {
          var result = await sendAsync(basicProperties, cancellationToken).ConfigureAwait(false);
          activity?.SetStatus(ActivityStatusCode.Ok);
          return result;
        }
        catch (Exception exception)
        {
          activity?.AddException(exception);
          activity?.SetStatus(ActivityStatusCode.Error);
          throw;
        }
      }
    }

    private static void InjectHeader(
      IDictionary<string, object?> headers,
      string key,
      string value
    ) => headers[key] = Encoding.UTF8.GetBytes(value);
  }
}
