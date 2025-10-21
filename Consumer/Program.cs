using System.Diagnostics;
using Consumer.Settings;
using DotNetAspireRabbitMq.ServiceDefaults;
using RabbitMQ.Client;

namespace Consumer;

internal sealed class Program
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

    builder.Services.AddSingleton<IConnection>(serviceProvider =>
    {
      var connectionFactory = serviceProvider.GetRequiredService<IConnectionFactory>();
      return connectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
    });

    var queueName = "weather";

    builder.Services.Configure<ConsumerOptions>(opts =>
    {
      var section = builder.Configuration.GetSection(ConsumerOptions.SectionName);

      if (section.Exists())
      {
        section.Bind(opts);

        if (string.IsNullOrWhiteSpace(opts.QueueName))
        {
          opts.QueueName = queueName;
        }
      }
      else
      {
        opts.QueueName = queueName;
        queueName = opts.QueueName;
      }
    });

    builder.Services.AddSingleton<IChannel>(serviceProvider =>
    {
      var connection = serviceProvider.GetRequiredService<IConnection>();

      var channel = connection.CreateChannelAsync().GetAwaiter().GetResult();

      channel
        .QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false)
        .GetAwaiter()
        .GetResult();

      channel.BasicQosAsync(0, 32, false).GetAwaiter().GetResult();

      return channel;
    });

    builder.Services.AddScoped<ConsumerActivity>(serviceProvider =>
    {
      var logger = serviceProvider.GetRequiredService<ILogger<ConsumerActivity>>();

      return new ConsumerActivity(new ActivitySource(builder.Environment.ApplicationName), logger);
    });

    builder.Services.AddHostedService<WeatherConsumer>();

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
