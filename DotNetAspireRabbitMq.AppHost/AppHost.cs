var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Producer>("producer");

builder.AddProject<Projects.Consumer>("consumer");

builder.Build().Run();
