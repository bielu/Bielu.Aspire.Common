using InfisicalConfiguration;
using Microsoft.Extensions.Configuration;
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

        if (string.IsNullOrEmpty(settings.ClientId) || string.IsNullOrEmpty(settings.ClientSecret))
        {
            throw new InvalidOperationException(
                $"{DefaultConfigSectionName}:ClientId and {DefaultConfigSectionName}:ClientSecret are required " +
                "for Universal Auth. Set them in configuration or via the configureSettings callback.");
        }

        // These values have been validated as non-null above.
        var projectId = settings.ProjectId!;
        var environment = settings.Environment!;
        var clientId = settings.ClientId!;
        var clientSecret = settings.ClientSecret!;

        var configBuilder = new InfisicalConfigBuilder()
            .SetProjectId(projectId)
            .SetEnvironment(environment)
            .SetSecretPath(settings.SecretPath)
            .SetInfisicalUrl(connectionString)
            .SetAuth(
                new InfisicalAuthBuilder()
                    .SetUniversalAuth(clientId, clientSecret)
                    .Build()
            );

        if (!string.IsNullOrEmpty(settings.Prefix))
        {
            configBuilder.SetPrefix(settings.Prefix);
        }

        builder.Configuration.AddInfisical(configBuilder.Build());

        return builder;
    }
}
