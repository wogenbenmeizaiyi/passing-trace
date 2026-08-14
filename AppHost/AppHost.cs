var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.PassingTrace_Identity_AuthorizationServer>("passingtrace-identity");

builder.Build().Run();
