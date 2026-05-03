using Infisical.Sdk;
using Infisical.Sdk.Model;
using JJConsulting.Infisical.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bielu.Aspire.Infisical.Client;

/// <summary>
/// Provides extension methods for wiring Infisical secrets into the .NET configuration system
/// and the official <see cref="InfisicalClient"/> SDK using a connection string resolved from
/// an Aspire resource reference.
/// </summary>
public static class InfisicalClientBuilderExtensions
{
    private const string DefaultConfigSectionName = "Infisical:Client";

    /// <summary>
    /// Adds an Infisical configuration provider that fetches secrets from an Infisical server
    /// whose URL is resolved from the Aspire connection string named <paramref name="connectionName"/>.
    /// <para>
    /// All Infisical settings (project ID, environment, auth credentials, etc.) are automatically
    /// populated from the <c>Infisical:Client</c> configuration section using the built-in
    /// <see cref="MachineIdentityInfisicalConfig.FromConfiguration"/> or
    /// <see cref="ServiceTokenInfisicalConfig.FromConfiguration"/> methods from
    /// <c>JJConsulting.Infisical</c>. The only value overridden by this method is the <c>Url</c>,
    /// which is resolved from the Aspire connection string.
    /// </para>
    /// <para>
    /// When the <c>ServiceToken</c> key is present in the configuration section, service-token
    /// authentication is used. Otherwise, machine identity authentication is used (requires
    /// <c>ClientId</c> and <c>ClientSecret</c>).
    /// </para>
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">
    /// The Aspire connection string name that resolves to the Infisical server URL
    /// (e.g., <c>http://localhost:8080</c>).
    /// </param>
    /// <param name="configureSettings">
    /// An optional callback to further configure <see cref="InfisicalClientSettings"/> before
    /// the Infisical config objects are built. Use this to set or override values that are not
    /// available in configuration (e.g., secrets from environment variables).
    /// </param>
    /// <returns>The <see cref="IHostApplicationBuilder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the connection string is not found or when the resulting configuration is invalid
    /// (missing project ID, environment, or auth credentials).
    /// </exception>
    public static IHostApplicationBuilder AddInfisicalConfiguration(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<InfisicalClientSettings>? configureSettings = null)
    {
        var (settings, connectionString, section) = ResolveSettings(builder, connectionName, configureSettings);

        // Determine auth mode: if ServiceToken is set, use service-token auth; otherwise machine identity.
        if (!string.IsNullOrEmpty(settings.ServiceToken))
        {
            var config = ServiceTokenInfisicalConfig.FromConfiguration(section);
            config = new ServiceTokenInfisicalConfig
            {
                ProjectId = settings.ProjectId ?? config.ProjectId,
                Environment = settings.Environment ?? config.Environment,
                SecretPath = settings.SecretPath ?? config.SecretPath,
                ServiceToken = settings.ServiceToken ?? config.ServiceToken,
                Url = connectionString
            };

            if (!config.IsValid())
            {
                throw new InvalidOperationException(
                    $"Infisical service-token configuration is invalid. " +
                    $"Ensure {DefaultConfigSectionName}:ServiceToken, ProjectId, and Environment are set.");
            }

            builder.Configuration.AddInfisical(config);
            builder.Services.AddInfisical(config);
        }
        else
        {
            var config = MachineIdentityInfisicalConfig.FromConfiguration(section);
            config = new MachineIdentityInfisicalConfig
            {
                ProjectId = settings.ProjectId ?? config.ProjectId,
                Environment = settings.Environment ?? config.Environment,
                SecretPath = settings.SecretPath ?? config.SecretPath,
                ClientId = settings.ClientId ?? config.ClientId,
                ClientSecret = settings.ClientSecret ?? config.ClientSecret,
                Url = connectionString
            };

            if (!config.IsValid())
            {
                throw new InvalidOperationException(
                    $"Infisical machine identity configuration is invalid. " +
                    $"Ensure {DefaultConfigSectionName}:ClientId, ClientSecret, ProjectId, and Environment are set.");
            }

            builder.Configuration.AddInfisical(config);
            builder.Services.AddInfisical(config);
        }

        return builder;
    }

    /// <summary>
    /// Registers the official Infisical .NET SDK <see cref="InfisicalClient"/> as a singleton
    /// in the service collection. The client is configured from the <c>Infisical:Client</c>
    /// configuration section, with the host URL resolved from the Aspire connection string
    /// named <paramref name="connectionName"/>.
    /// <para>
    /// When <c>ClientId</c> and <c>ClientSecret</c> are available, the client is automatically
    /// authenticated using Universal Auth at registration time.
    /// </para>
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">
    /// The Aspire connection string name that resolves to the Infisical server URL.
    /// </param>
    /// <param name="configureSettings">
    /// An optional callback to further configure <see cref="InfisicalClientSettings"/>
    /// before the SDK client is created.
    /// </param>
    /// <returns>The <see cref="IHostApplicationBuilder"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the connection string is not found.
    /// </exception>
    public static IHostApplicationBuilder AddInfisicalClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<InfisicalClientSettings>? configureSettings = null)
    {
        var (settings, connectionString, _) = ResolveSettings(builder, connectionName, configureSettings);

        builder.Services.AddSingleton<InfisicalClient>(_ =>
        {
            var sdkSettings = new InfisicalSdkSettingsBuilder()
                .WithHostUri(connectionString)
                .Build();

            var client = new InfisicalClient(sdkSettings);

            if (!string.IsNullOrEmpty(settings.ClientId) && !string.IsNullOrEmpty(settings.ClientSecret))
            {
                client.Auth().UniversalAuth()
                    .LoginAsync(settings.ClientId, settings.ClientSecret)
                    .GetAwaiter().GetResult();
            }

            return client;
        });

        return builder;
    }

    /// <summary>
    /// Adds both the Infisical configuration provider (via
    /// <see cref="AddInfisicalConfiguration"/>) and the official Infisical SDK
    /// <see cref="InfisicalClient"/> (via <see cref="AddInfisicalClient"/>) using the same
    /// underlying settings resolved from the Aspire connection string and the
    /// <c>Infisical:Client</c> configuration section.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="connectionName">
    /// The Aspire connection string name that resolves to the Infisical server URL.
    /// </param>
    /// <param name="configureSettings">
    /// An optional callback to further configure <see cref="InfisicalClientSettings"/>.
    /// </param>
    /// <returns>The <see cref="IHostApplicationBuilder"/> for chaining.</returns>
    public static IHostApplicationBuilder AddInfisical(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<InfisicalClientSettings>? configureSettings = null)
    {
        builder.AddInfisicalConfiguration(connectionName, configureSettings);
        builder.AddInfisicalClient(connectionName, configureSettings);
        return builder;
    }

    private static (InfisicalClientSettings Settings, string ConnectionString, IConfigurationSection Section) ResolveSettings(
        IHostApplicationBuilder builder,
        string connectionName,
        Action<InfisicalClientSettings>? configureSettings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);

        var connectionString = builder.Configuration.GetConnectionString(connectionName);

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{connectionName}' not found. " +
                "Ensure the Infisical resource is referenced via .WithReference() in the AppHost.");
        }

        var section = builder.Configuration.GetSection(DefaultConfigSectionName);

        var settings = new InfisicalClientSettings
        {
            ProjectId = section["ProjectId"],
            Environment = section["Environment"],
            SecretPath = section["SecretPath"],
            ServiceToken = section["ServiceToken"],
            ClientId = section["ClientId"],
            ClientSecret = section["ClientSecret"]
        };
        configureSettings?.Invoke(settings);

        return (settings, connectionString, section);
    }
}
