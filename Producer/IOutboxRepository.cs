namespace Producer
{
  public interface IOutboxRepository
  {
    Task AddToOutboxAsync<T>(T message);
  }
}
