using RabbitMQ.Client;

namespace Producer;

public class Program
{
  public static void Main(string[] args)
  {
    var builder = WebApplication.CreateBuilder(args);

    builder.AddServiceDefaults();

    builder.Services.AddControllers();

    builder.Services.AddOpenApi();

    if (builder.Environment.IsDevelopment())
    {
      builder.AddRabbitMQClient("rabbitmq");
    }

    // Register RabbitMQ connection and channel
    builder.Services.AddSingleton<IConnection>(serviceProvider =>
    {
      var connectionFactory = serviceProvider.GetRequiredService<IConnectionFactory>();
      return connectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
    });

    builder.Services.AddSingleton<IChannel>(serviceProvider =>
    {
      var connection = serviceProvider.GetRequiredService<IConnection>();

      var channelOpts = new CreateChannelOptions(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true,
        outstandingPublisherConfirmationsRateLimiter: new ThrottlingRateLimiter(10)
      );

      var channel = connection.CreateChannelAsync(channelOpts).GetAwaiter().GetResult();

      const string queueName = "weather";

      channel
        .QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false)
        .GetAwaiter()
        .GetResult();

      return channel;
    });

    var app = builder.Build();

    app.MapDefaultEndpoints();

    if (app.Environment.IsDevelopment())
    {
      app.MapOpenApi();
    }

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();

    app.Run();
  }
}
