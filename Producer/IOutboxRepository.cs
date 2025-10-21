using Producer.Entities;

namespace Producer
{
  public interface IOutboxRepository
  {
    Task AddToOutboxAsync<T>(T message);
    Task<IEnumerable<OutboxMessage>> GetUnprocessedMessagesAsync(int limit);
    Task MarkAsErrorAsync(Guid messageId, string errorMessage);
    Task MarkAsProcessedAsync(Guid messageId);
  }
}
