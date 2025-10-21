using System.Diagnostics;
using DotNetAspireRabbitMq.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Producer.Database;
using Producer.Settings;
using RabbitMQ.Client;

namespace Producer;

internal sealed class Program
{
  public static async Task Main(string[] args)
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

    builder.Services.Configure<PublisherOptions>(opts =>
    {
      var defaultPeriod = TimeSpan.FromSeconds(10);
      var defaultPublisherConfirmsTimeout = TimeSpan.FromSeconds(5);

      var section = builder.Configuration.GetSection(PublisherOptions.SectionName);

      if (section.Exists())
      {
        section.Bind(opts);

        if (string.IsNullOrWhiteSpace(opts.QueueName))
        {
          opts.QueueName = queueName;
        }

        if (opts.Period <= TimeSpan.Zero)
        {
          opts.Period = defaultPeriod;
        }

        if (opts.PublisherConfirmsTimeout <= TimeSpan.Zero)
        {
          opts.PublisherConfirmsTimeout = defaultPublisherConfirmsTimeout;
        }
      }
      else
      {
        opts.Period = defaultPeriod;
        opts.PublisherConfirmsTimeout = defaultPublisherConfirmsTimeout;
        opts.QueueName = queueName;
        queueName = opts.QueueName;
      }
    });

    builder.Services.Configure<OutboxOptions>(opts =>
    {
      var defaultBatchSize = 100;

      var section = builder.Configuration.GetSection(OutboxOptions.SectionName);

      if (section.Exists())
      {
        section.Bind(opts);

        if (opts.BatchSize < 1)
        {
          opts.BatchSize = defaultBatchSize;
        }
      }
      else
      {
        opts.BatchSize = defaultBatchSize;
      }
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

      channel
        .QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false)
        .GetAwaiter()
        .GetResult();

      return channel;
    });

    builder.Services.AddDbContext<ProducerDbContext>(opts =>
    {
      var connectionString =
        builder.Configuration.GetConnectionString("producerdb")
        ?? throw new InvalidOperationException("Database connection string was not specified.");

      opts.UseSqlServer(connectionString).UseSnakeCaseNamingConvention();

      if (builder.Environment.IsDevelopment())
      {
        opts.EnableSensitiveDataLogging();
      }
    });

    builder.EnrichSqlServerDbContext<ProducerDbContext>(settings =>
    {
      settings.DisableTracing = false;
      settings.DisableHealthChecks = false;
      settings.DisableRetry = true;
    });

    builder.AddSqlServerClient("producerdb");

    builder.Services.AddScoped<PublisherActivity>(serviceProvider =>
    {
      var logger = serviceProvider.GetRequiredService<ILogger<PublisherActivity>>();
      return new PublisherActivity(new ActivitySource(builder.Environment.ApplicationName), logger);
    });

    builder.Services.AddHostedService<WeatherPublisher>();
    builder.Services.AddSingleton<PublisherConfirmationTracker>();
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();

    var app = builder.Build();

    app.MapDefaultEndpoints();

    if (app.Environment.IsDevelopment())
    {
      app.MapOpenApi();
      await app.ApplyMigrationsAsync();
    }

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();

    await app.RunAsync();
  }
}
