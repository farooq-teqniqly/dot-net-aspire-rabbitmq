namespace Consumer;

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
      builder.AddRabbitMQClient(
        "rabbitmq",
        static settings =>
        {
          settings.DisableHealthChecks = false;
          settings.DisableTracing = false;
          settings.DisableAutoActivation = false;
        }
      );
    }

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
