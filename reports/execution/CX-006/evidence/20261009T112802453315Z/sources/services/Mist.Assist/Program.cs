var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { provider = "disabled", world_write_capability = false }));
app.MapPost("/v1/assist", () => Results.Json(new { code = "provider_disabled" }, statusCode: 503));
app.Run();
