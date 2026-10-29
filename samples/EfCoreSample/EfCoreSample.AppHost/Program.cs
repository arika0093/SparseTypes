var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL
var postgres = builder.AddPostgres("postgres").WithPgWeb();
var orders = postgres.AddDatabase("orders");

// WebServer (ASP.NET Core/EF Core)
var server = builder
    .AddProject<Projects.EfCoreSample_Server>("server")
    .WithReference(orders)
    .WaitFor(orders)
    .WithHttpEndpoint(port: 5000, isProxied: false)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

// C# Client (WPF)
builder
    .AddProject<Projects.EfCoreSample_WinAppClient>("client")
    .WithReference(server)
    .WaitFor(server)
    .WithExplicitStart();

// JavaScript Client (Vite/React)
builder
    .AddJavaScriptApp("web", "../EfCoreSample.WebClient", "dev")
    .WithReference(server)
    .WithEnvironment("API_URL", server.GetEndpoint("http"))
    .WithHttpEndpoint(port: 5173, name: "http", isProxied: false)
    .WithExternalHttpEndpoints()
    .WaitFor(server);

// Execute the application
builder.Build().Run();
