using Bielu.Aspire.Infisical.Client;
using Infisical.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        // Optionally wire Infisical (configuration provider + SDK client) when an Aspire
        // connection string named "infisical" is present. This mirrors the pattern used by
        // other Aspire client integrations and lets every service share the same secrets.
        if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("infisical")))
        {
            builder.AddInfisical("infisical");
        }

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Configures Kestrel to load its TLS certificate from Infisical and (optionally)
    /// negotiate the desired HTTP protocol versions. Requires that
    /// <see cref="AddServiceDefaults{TBuilder}"/> already registered the Infisical SDK client
    /// (i.e. an <c>infisical</c> connection string is present).
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="pfxSecretName">Infisical secret holding the Base64-encoded PFX.</param>
    /// <param name="passwordSecretName">Infisical secret holding the PFX password (optional).</param>
    /// <param name="protocols">
    /// HTTP protocol versions to enable on the HTTPS endpoint. Defaults to HTTP/1 + HTTP/2.
    /// </param>
    public static WebApplicationBuilder UseInfisicalKestrelHttps(
        this WebApplicationBuilder builder,
        string pfxSecretName,
        string? passwordSecretName = null,
        HttpProtocols protocols = HttpProtocols.Http1AndHttp2)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(pfxSecretName);

        builder.WebHost.ConfigureKestrel((context, kestrel) =>
        {
            var sp = kestrel.ApplicationServices;
            var client = sp.GetRequiredService<InfisicalClient>();

            var section = context.Configuration.GetSection("Infisical:Client");
            var projectId = section["ProjectId"]
                ?? throw new InvalidOperationException("Infisical:Client:ProjectId is required to load HTTPS certificate.");
            var environment = section["Environment"]
                ?? throw new InvalidOperationException("Infisical:Client:Environment is required to load HTTPS certificate.");
            var secretPath = section["SecretPath"] ?? "/";

            kestrel.ConfigureHttpsDefaults(https =>
            {
                // Eagerly load the certificate from Infisical and reuse it for every HTTPS endpoint.
                https.ServerCertificate = InfisicalKestrelExtensions
                    .LoadCertificateAsync(client, pfxSecretName, passwordSecretName, projectId, environment, secretPath)
                    .GetAwaiter().GetResult();
            });

            kestrel.ConfigureEndpointDefaults(listen =>
            {
                listen.Protocols = protocols;
            });
        });

        return builder;
    }

    /// <summary>
    /// Configures Kestrel HTTPS using certificate metadata read from <c>appsettings.json</c>,
    /// loading the actual PFX bytes (and optional password) from Infisical secrets.
    /// <para>
    /// Mirrors the standard ASP.NET Core <c>Kestrel:Certificates:Default</c> shape, but instead
    /// of reading the certificate from disk it uses the configured <c>Path</c> as the
    /// <em>Infisical secret name</em> that holds the Base64-encoded PFX, and
    /// <c>PasswordSecret</c> as the secret name that holds the PFX password.
    /// </para>
    /// <para>Example <c>appsettings.json</c>:</para>
    /// <code>
    /// "Kestrel": {
    ///   "Certificates": {
    ///     "Default": {
    ///       "Path": "my-app.pfx",         // Infisical secret name (Base64 PFX)
    ///       "PasswordSecret": "my-app.pfx.password", // Infisical secret name (plain text)
    ///       "Protocols": "Http1AndHttp2AndHttp3"      // optional
    ///     }
    ///   }
    /// }
    /// </code>
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="configurationKey">
    /// Configuration key for the certificate entry. Defaults to <c>Kestrel:Certificates:Default</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the configuration entry was found and HTTPS was wired up; otherwise <c>false</c>.
    /// </returns>
    public static bool UseInfisicalKestrelHttpsFromConfiguration(
        this WebApplicationBuilder builder,
        string configurationKey = "Kestrel:Certificates:Default")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(configurationKey);

        var certSection = builder.Configuration.GetSection(configurationKey);
        var pfxSecretName = certSection["Path"] ?? certSection["Name"];
        if (string.IsNullOrEmpty(pfxSecretName))
        {
            return false;
        }

        var passwordSecretName = certSection["PasswordSecret"] ?? certSection["PasswordName"];

        var protocols = HttpProtocols.Http1AndHttp2;
        var protocolsRaw = certSection["Protocols"];
        if (!string.IsNullOrEmpty(protocolsRaw)
            && Enum.TryParse<HttpProtocols>(protocolsRaw, ignoreCase: true, out var parsed))
        {
            protocols = parsed;
        }

        builder.UseInfisicalKestrelHttps(pfxSecretName, passwordSecretName, protocols);
        return true;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}
