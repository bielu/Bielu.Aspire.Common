using Bielu.Aspire.Common.Example.Blazor.Web;
using Bielu.Aspire.Common.Example.Blazor.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
// When the AppHost references the Infisical resource (.WithReference(infisical)),
// AddServiceDefaults() will automatically register the Infisical configuration provider
// and the Infisical SDK client (InfisicalClient) using the "infisical" connection string.
builder.AddServiceDefaults();

// Optional: load the Kestrel HTTPS certificate (PFX + password) directly from Infisical
// and choose the HTTP protocol versions to enable. Requires secrets named
// "Kestrel__Pfx" (Base64-encoded PFX) and "Kestrel__PfxPassword" in the configured
// Infisical project/environment.
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
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddHttpClient<WeatherApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://apiservice");
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
