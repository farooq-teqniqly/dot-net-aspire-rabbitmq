namespace Producer.Entities
{
  public sealed class OutboxMessage
  {
    public required string Content { get; init; }
    public string? Error { get; set; }
    public required Guid Id { get; init; }
    public required DateTimeOffset OccurredOnUtc { get; init; }
    public DateTimeOffset? ProcessedOnUtc { get; set; }
  }
}
