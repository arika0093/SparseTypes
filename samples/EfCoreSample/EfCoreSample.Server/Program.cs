using EfCoreSample.Contracts;
using EfCoreSample.Server;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore;
using SparseTypes.OpenApi.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5000");
builder.Services.AddDbContext<SampleDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("orders") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=orders"
    )
);
builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddSparseEntityMappers(typeof(OrderMapper));
builder.Services.AddOpenApi();

// The OrderDto bundle includes schemas for its nested address and line models.
builder.Services.AddSparseOpenApi(OrderDto.PayloadJson.Schema);
var app = builder.Build();
app.Services.ValidateSparseEntityMappers<SampleDbContext>();
SampleDbInitializer.Initialize(app.Services);

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "EfCoreSample v1");
    options.RoutePrefix = string.Empty;
});
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
