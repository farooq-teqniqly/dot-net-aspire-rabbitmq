namespace Consumer.Settings
{
  public class ConsumerOptions
  {
    public const string SectionName = "Consumer";
    public required string QueueName { get; set; }
  }
}
