var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres").WithPgWeb();
var orders = postgres.AddDatabase("orders");

var server = builder
    .AddProject<Projects.EfCoreSample_Server>("server")
    .WithReference(orders)
    .WaitFor(orders)
    .WithHttpEndpoint(port: 5000, isProxied: false)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");
builder
    .AddProject<Projects.EfCoreSample_Client>("client")
    .WithReference(server)
    .WaitFor(server)
    .WithExplicitStart();
builder.Build().Run();
