using System.Text.Json;
using Dapper;
using Producer.Entities;

namespace Producer
{
  internal sealed class OutboxRepository : IOutboxRepository
  {
    private readonly ILogger<OutboxRepository> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public OutboxRepository(IUnitOfWork unitOfWork, ILogger<OutboxRepository> logger)
    {
      ArgumentNullException.ThrowIfNull(unitOfWork);
      ArgumentNullException.ThrowIfNull(logger);

      _unitOfWork = unitOfWork;
      _logger = logger;
    }

    public async Task AddToOutboxAsync<T>(T message)
    {
      var outboxMessage = new OutboxMessage
      {
        Id = Guid.CreateVersion7(),
        Content = JsonSerializer.Serialize(message),
        OccurredOnUtc = DateTimeOffset.UtcNow,
      };

      var sql =
        "INSERT INTO dbo.outbox_messages (id, content, occurred_on_utc) VALUES (@Id, @Content, @OccurredOnUtc)";

      var rowsInserted = await _unitOfWork
        .Transaction.Connection!.ExecuteAsync(
          sql,
          outboxMessage,
          transaction: _unitOfWork.Transaction
        )
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

    public async Task<IEnumerable<OutboxMessage>> GetUnprocessedMessagesAsync(int batchSize)
    {
      var sql =
        $"SELECT TOP ({batchSize}) id, content FROM dbo.outbox_messages WITH (READPAST) WHERE processed_on_utc IS NULL ORDER BY occurred_on_utc";

      _logger.LogInformation("Getting outbox messages. {CommandText}", sql);

      var messages = await _unitOfWork
        .Transaction.Connection!.QueryAsync<OutboxMessage>(
          sql,
          transaction: _unitOfWork.Transaction
        )
        .ConfigureAwait(false);

      return messages;
    }

    public async Task MarkAsErrorAsync(Guid messageId, string errorMessage)
    {
      var sql =
        "UPDATE dbo.outbox_messages SET processed_on_utc = @ProcessedOnUtc, error = @Error WHERE id = @Id";

      await _unitOfWork
        .Transaction.Connection!.ExecuteAsync(
          sql,
          new
          {
            ProcessedOnUtc = DateTimeOffset.UtcNow,
            Error = errorMessage,
            Id = messageId,
          },
          transaction: _unitOfWork.Transaction
        )
        .ConfigureAwait(false);

      _logger.LogInformation("Marked outbox message as error. CommandText: {CommandText}", sql);
    }

    public async Task MarkAsProcessedAsync(Guid messageId)
    {
      var sql = "UPDATE dbo.outbox_messages SET processed_on_utc = @ProcessedOnUtc WHERE id = @Id";

      await _unitOfWork
        .Transaction.Connection!.ExecuteAsync(
          sql,
          new { ProcessedOnUtc = DateTimeOffset.UtcNow, Id = messageId },
          transaction: _unitOfWork.Transaction
        )
        .ConfigureAwait(false);

      _logger.LogInformation("Marked outbox message as processed. CommandText: {CommandText}", sql);
    }
  }
}
