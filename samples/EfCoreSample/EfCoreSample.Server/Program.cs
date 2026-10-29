using System.Reflection;
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
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.TypeInfoResolverChain.Add(OrderDto.PayloadJson.Resolver)
);
builder.Services.AddHealthChecks();
builder.Services.AddSparseEntityMappers(typeof(OrderMapper));
builder.Services.AddOpenApi();

// Keyed line item endpoints contain the DTO snapshot, so register its schema explicitly.
builder.Services.AddSparseOpenApi(OrderDto.PayloadJson.Schema, OrderApiMetadata.ExternalPayloadSchemas);
var app = builder.Build();
if (Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
{
    app.Services.ValidateSparseEntityMappers<SampleDbContext>();
    SampleDbInitializer.Initialize(app.Services);
}

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "EfCoreSample v1");
    options.RoutePrefix = string.Empty;
});
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
