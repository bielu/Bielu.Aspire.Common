using JJConsulting.Infisical.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bielu.Aspire.Infisical.Client;

/// <summary>
/// Provides extension methods for wiring Infisical secrets into the .NET configuration system
/// using a connection string resolved from an Aspire resource reference.
/// </summary>
public static class InfisicalClientBuilderExtensions
{
    private const string DefaultConfigSectionName = "Infisical:Client";

    /// <summary>
    /// Adds an Infisical configuration provider that fetches secrets from an Infisical server
    /// whose URL is resolved from the Aspire connection string named <paramref name="connectionName"/>.
    /// Additional settings (project ID, environment, auth, etc.) are read from the
    /// <c>Infisical:Client</c> configuration section and can be overridden via the
    /// <paramref name="configureSettings"/> callback.
    /// <para>
    /// When <see cref="InfisicalClientSettings.ServiceToken"/> is set, service-token authentication
    /// is used. Otherwise, machine identity authentication is used with
    /// <see cref="InfisicalClientSettings.ClientId"/> and <see cref="InfisicalClientSettings.ClientSecret"/>.
    /// </para>
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">
    /// The Aspire connection string name that resolves to the Infisical server URL
    /// (e.g., <c>http://localhost:8080</c>).
    /// </param>
    /// <param name="configureSettings">
    /// An optional callback to further configure <see cref="InfisicalClientSettings"/> after
    /// they have been bound from configuration.
    /// </param>
    /// <returns>The <see cref="IHostApplicationBuilder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when required settings (<see cref="InfisicalClientSettings.ProjectId"/>,
    /// <see cref="InfisicalClientSettings.Environment"/>, or auth credentials) are missing.
    /// </exception>
    public static IHostApplicationBuilder AddInfisicalConfiguration(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<InfisicalClientSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);

        var settings = new InfisicalClientSettings();
        builder.Configuration.GetSection(DefaultConfigSectionName).Bind(settings);
        configureSettings?.Invoke(settings);

        var connectionString = builder.Configuration.GetConnectionString(connectionName);

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{connectionName}' not found. " +
                "Ensure the Infisical resource is referenced via .WithReference() in the AppHost.");
        }

        if (string.IsNullOrEmpty(settings.ProjectId))
        {
            throw new InvalidOperationException(
                $"{DefaultConfigSectionName}:ProjectId is required. " +
                "Set it in configuration or via the configureSettings callback.");
        }

        if (string.IsNullOrEmpty(settings.Environment))
        {
            throw new InvalidOperationException(
                $"{DefaultConfigSectionName}:Environment is required. " +
                "Set it in configuration or via the configureSettings callback.");
        }

        if (!string.IsNullOrEmpty(settings.ServiceToken))
        {
            var config = new ServiceTokenInfisicalConfig
            {
                ProjectId = settings.ProjectId,
                Environment = settings.Environment,
                SecretPath = settings.SecretPath,
                Url = connectionString,
                ServiceToken = settings.ServiceToken
            };

            builder.Configuration.AddInfisical(config);
            builder.Services.AddInfisical(config);
        }
        else
        {
            if (string.IsNullOrEmpty(settings.ClientId) || string.IsNullOrEmpty(settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    $"{DefaultConfigSectionName}:ClientId and {DefaultConfigSectionName}:ClientSecret are required " +
                    "for machine identity auth (or set {DefaultConfigSectionName}:ServiceToken for service-token auth). " +
                    "Set them in configuration or via the configureSettings callback.");
            }

            var config = new MachineIdentityInfisicalConfig
            {
                ProjectId = settings.ProjectId,
                Environment = settings.Environment,
                SecretPath = settings.SecretPath,
                Url = connectionString,
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            };

            builder.Configuration.AddInfisical(config);
            builder.Services.AddInfisical(config);
        }

        return builder;
    }
}
