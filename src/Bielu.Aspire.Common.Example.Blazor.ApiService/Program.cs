var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
// When the AppHost references the Infisical resource (.WithReference(infisical)),
// AddServiceDefaults() will automatically register the Infisical configuration provider
// and the Infisical SDK client (InfisicalClient) using the "infisical" connection string.
builder.AddServiceDefaults();

// Optional: load the Kestrel HTTPS certificate (PFX + password) from Infisical and
// pick the HTTP protocol versions to enable on the HTTPS endpoint.
//
// builder.UseInfisicalKestrelHttps(
//     pfxSecretName: "Kestrel__Pfx",
//     passwordSecretName: "Kestrel__PfxPassword",
//     protocols: Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2AndHttp3);

// Or, drive it from appsettings.json using the standard Kestrel:Certificates:Default shape,
// where "Path" is the Infisical secret name holding the Base64-encoded PFX and
// "PasswordSecret" is the Infisical secret name holding the password.
builder.UseInfisicalKestrelHttpsFromConfiguration();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /weatherforecast to see sample data.");

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

app.Run();

internal sealed record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
