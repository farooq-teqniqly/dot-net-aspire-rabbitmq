using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Producer.Entities;

namespace Producer
{
  internal sealed class OutboxRepository : IOutboxRepository
  {
    private readonly ILogger<OutboxRepository> _logger;
    private readonly SqlConnection _sqlConnection;

    public OutboxRepository(SqlConnection sqlConnection, ILogger<OutboxRepository> logger)
    {
      ArgumentNullException.ThrowIfNull(sqlConnection);
      ArgumentNullException.ThrowIfNull(logger);

      _sqlConnection = sqlConnection;
      _logger = logger;
    }

    public async Task AddToOutboxAsync<T>(T message)
    {
      var outboxMessage = new OutboxMessage
      {
        Id = Guid.CreateVersion7(),
        Type =
          typeof(T).FullName
          ?? throw new InvalidOperationException($"Could not set {nameof(OutboxMessage.Type)}"),
        Content = JsonSerializer.Serialize(message),
        OccurredOnUtc = DateTimeOffset.UtcNow,
      };

      var sql =
        "INSERT INTO dbo.outbox_messages (id, type, content, occurred_on_utc) VALUES (@Id, @Type, @Content, @OccurredOnUtc)";

      var rowsInserted = await _sqlConnection
        .ExecuteAsync(sql, outboxMessage)
        .ConfigureAwait(false);

      if (rowsInserted < 1)
      {
        throw new InvalidOperationException("Failed to add message to outbox database.");
      }

      _logger.LogInformation(
        "Message {MessageId} added to outbox database. {CommandText}",
        outboxMessage.Id,
        sql
      );
    }

    public async Task<IEnumerable<OutboxMessage>> GetUnprocessedMessagesAsync(
      int limit,
      DbTransaction transaction
    )
    {
      var sql =
        $"SELECT TOP ({limit}) id, type, content FROM dbo.outbox_messages WITH (READPAST) WHERE processed_on_utc IS NULL ORDER BY occurred_on_utc";

      _logger.LogInformation("Getting outbox messages. {CommandText}", sql);

      var messages = await _sqlConnection
        .QueryAsync<OutboxMessage>(sql, transaction: transaction)
        .ConfigureAwait(false);

      return messages;
    }

    public async Task MarkAsErrorAsync(
      Guid messageId,
      string errorMessage,
      DbTransaction transaction
    )
    {
      var sql =
        "UPDATE dbo.outbox_messages SET processed_on_utc = @ProcessedOnUtc, error = @Error WHERE id = @Id";

      await _sqlConnection
        .ExecuteAsync(
          sql,
          new
          {
            ProcessedOnUtc = DateTimeOffset.UtcNow,
            Error = errorMessage,
            Id = messageId,
          },
          transaction: transaction
        )
        .ConfigureAwait(false);

      _logger.LogInformation(
        "Marked outbox message as unprocessed. CommandText: {CommandText}",
        sql
      );
    }

    public async Task MarkAsProcessedAsync(Guid messageId, DbTransaction transaction)
    {
      var sql = "UPDATE dbo.outbox_messages SET processed_on_utc = @ProcessedOnUtc WHERE id = @Id";

      await _sqlConnection
        .ExecuteAsync(
          sql,
          new { ProcessedOnUtc = DateTimeOffset.UtcNow, Id = messageId },
          transaction: transaction
        )
        .ConfigureAwait(false);

      _logger.LogInformation("Marked outbox message as processed. CommandText: {CommandText}", sql);
    }
  }
}
