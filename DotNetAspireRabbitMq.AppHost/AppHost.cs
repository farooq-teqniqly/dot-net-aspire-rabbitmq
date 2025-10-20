var builder = DistributedApplication.CreateBuilder(args);

var username = builder.AddParameter("RabbitMQ-username", secret: true);
var password = builder.AddParameter("RabbitMQ-password", secret: true);

var rabbitMq = builder
  .AddRabbitMQ("rabbitmq", username, password)
  .WithDataVolume(isReadOnly: false)
  .WithLifetime(ContainerLifetime.Persistent);

if (builder.ExecutionContext.IsRunMode)
{
  rabbitMq.WithManagementPlugin();
}

builder.AddProject<Projects.Producer>("producer").WithReference(rabbitMq).WaitFor(rabbitMq);

builder.AddProject<Projects.Consumer>("consumer").WithReference(rabbitMq).WaitFor(rabbitMq);

builder.Build().Run();
