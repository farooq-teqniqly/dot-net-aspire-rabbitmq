using System.Data.Common;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Producer.Entities;
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
      CancellationToken cancellationToken = default
    )
    {
      using (var scope = _serviceScopeFactory.CreateScope())
      {
        var publisherActivity = scope.ServiceProvider.GetRequiredService<PublisherActivity>();

        await publisherActivity
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
      var queryMessagesSql =
        "SELECT TOP (2) id, type, content FROM dbo.outbox_messages WITH (READPAST) WHERE processed_on_utc IS NULL ORDER BY occurred_on_utc";

      var updateSql =
        "UPDATE dbo.outbox_messages SET processed_on_utc = @ProcessedOnUtc WHERE id = @Id";

      while (!stoppingToken.IsCancellationRequested)
      {
        using (var scope = _serviceScopeFactory.CreateScope())
        {
          var sqlConnection = scope.ServiceProvider.GetRequiredService<SqlConnection>();

          await sqlConnection.OpenAsync(stoppingToken).ConfigureAwait(false);

          var transaction = await sqlConnection
            .BeginTransactionAsync(stoppingToken)
            .ConfigureAwait(false);

          await using (transaction)
          {
            _logger.LogInformation("Getting outbox messages. {CommandText}", queryMessagesSql);

            var messages = await sqlConnection
              .QueryAsync<OutboxMessage>(queryMessagesSql, transaction: transaction)
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

                await PublishForecastAsync(weatherForecast, stoppingToken).ConfigureAwait(false);

                await sqlConnection
                  .ExecuteAsync(
                    updateSql,
                    new { ProcessedOnUtc = DateTimeOffset.UtcNow, message.Id },
                    transaction: transaction
                  )
                  .ConfigureAwait(false);

                _logger.LogInformation(
                  "Marking outbox message as processed. CommandText: {CommandText}",
                  updateSql
                );
              }
              catch (InvalidOperationException exception)
              {
                await SetOutboxMessageError(message.Id, exception, sqlConnection, transaction)
                  .ConfigureAwait(false);
              }
              catch (JsonException exception)
              {
                await SetOutboxMessageError(message.Id, exception, sqlConnection, transaction)
                  .ConfigureAwait(false);
              }
            }

            await transaction.CommitAsync(stoppingToken).ConfigureAwait(false);
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

    private async Task SetOutboxMessageError(
      Guid messageId,
      Exception exception,
      SqlConnection sqlConnection,
      DbTransaction transaction
    )
    {
      _logger.LogError(exception, "Failed to process outbox message.");

      var sql =
        "UPDATE dbo.outbox_messages SET processed_on_utc = @ProcessedOnUtc, error = @Error WHERE id = @Id";

      _logger.LogInformation(
        "Marking outbox message as unprocessed. CommandText: {CommandText}",
        sql
      );

      await sqlConnection
        .ExecuteAsync(
          sql,
          new
          {
            ProcessedOnUtc = DateTimeOffset.UtcNow,
            Error = exception.Message,
            Id = messageId,
          },
          transaction: transaction
        )
        .ConfigureAwait(false);
    }
  }
}
