using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;

var builder = DistributedApplication.CreateBuilder(args);
var postgresPassword = builder.AddParameter(
    "postgres-password",
    secret: true);


var postgres = builder.AddPostgres("postgres")
    .WithHostPort(5432)
    .WithPassword(postgresPassword)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var userDataBase = postgres.AddDatabase("user");

builder.AddProject<Projects.PassingTrace_Identity_AuthorizationServer>("passingtrace-identity")
    .WithReference(userDataBase)
    .WaitFor(userDataBase);

builder.Build().Run();
