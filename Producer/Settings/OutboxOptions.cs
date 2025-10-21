namespace Producer.Settings
{
  public sealed class OutboxOptions
  {
    public const string SectionName = "Outbox";
    public required int BatchSize { get; set; }
  }
}
