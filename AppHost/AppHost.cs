var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.PassingTrace_Auth>("passingtrace-auth");

builder.Build().Run();
