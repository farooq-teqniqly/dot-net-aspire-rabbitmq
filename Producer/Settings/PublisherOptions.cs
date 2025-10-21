namespace Producer.Settings
{
  public sealed class PublisherOptions
  {
    public const string SectionName = "Publisher";

    public required TimeSpan Period { get; set; }
    public required TimeSpan PublisherConfirmsTimeout { get; set; }
    public required string QueueName { get; set; }
  }
}
