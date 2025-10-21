using System.Data.Common;
using Producer.Entities;

namespace Producer
{
  public interface IOutboxRepository
  {
    Task AddToOutboxAsync<T>(T message);
    Task<IEnumerable<OutboxMessage>> GetUnprocessedMessagesAsync(
      int limit,
      DbTransaction transaction
    );
    Task MarkAsErrorAsync(Guid messageId, string errorMessage, DbTransaction transaction);
    Task MarkAsProcessedAsync(Guid messageId, DbTransaction transaction);
  }
}
