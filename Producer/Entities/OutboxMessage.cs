namespace Producer.Entities
{
  public sealed class OutboxMessage
  {
    public required string Content { get; set; }
    public string? Error { get; set; }
    public required Guid Id { get; set; }
    public required DateTimeOffset OccurredOnUtc { get; set; }
    public DateTimeOffset? ProcessedOnUtc { get; set; }
    public required string Type { get; set; }
  }
}
