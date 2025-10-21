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

var sql = builder
  .AddSqlServer("sql")
  .WithLifetime(ContainerLifetime.Persistent)
  .WithImage("mssql/server:2022-CU21-ubuntu-22.04");

var db = sql.AddDatabase("producerdb", "producer");

builder
  .AddProject<Projects.Producer>("producer")
  .WithReference(rabbitMq)
  .WaitFor(rabbitMq)
  .WithReference(db)
  .WaitFor(db);

builder.AddProject<Projects.Consumer>("consumer").WithReference(rabbitMq).WaitFor(rabbitMq);

builder.Build().Run();
