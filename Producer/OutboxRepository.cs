using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Producer.Entities;

namespace Producer
{
  internal sealed class OutboxRepository : IOutboxRepository
  {
    private readonly SqlConnection _sqlConnection;
    private readonly ILogger<OutboxRepository> _logger;

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
        "Inserted {RowsInserted} row(s) to outbox database. {CommandText}",
        rowsInserted,
        sql
      );
    }
  }
}
