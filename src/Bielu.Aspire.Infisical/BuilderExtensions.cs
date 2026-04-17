using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace Bielu.Aspire.Infisical;

public static class BuilderExtensions
{
    /// <summary>
    /// Adds an Infisical secrets management container to the distributed application.
    /// Requires PostgreSQL and a Redis-compatible cache (Redis, Valkey, etc.) connection
    /// details to be configured via the <c>Infisical</c> configuration section, or provided as parameters.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name for the Infisical container.</param>
    /// <param name="port">Optional host port to map to Infisical's internal port (8080).</param>
    /// <param name="imageTag">The Infisical Docker image tag. Defaults to <c>latest</c>.</param>
    /// <returns>A resource builder for the Infisical container.</returns>
    public static IResourceBuilder<InfisicalResource> AddInfisical(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "infisical",
        int? port = null,
        string imageTag = "latest")
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = ReadInfisicalSettings(builder);

        var infisicalConfig = builder.Configuration.GetSection("Infisical");

        var dbConnectionUri = infisicalConfig.GetValue<string>("DbConnectionUri")
                              ?? throw new InvalidOperationException(
                                  "Infisical:DbConnectionUri configuration is required. " +
                                  "Example: postgresql://user:password@host:5432/infisical");

        var redisUrl = infisicalConfig.GetValue<string>("RedisUrl")
                       ?? throw new InvalidOperationException(
                           "Infisical:RedisUrl configuration is required. " +
                           "Example: redis://host:6379");

        var infisical = new InfisicalResource(name);

        var container = builder.AddResource(infisical)
            .WithImage("infisical/infisical", imageTag)
            .WithHttpEndpoint(port: port, targetPort: 8080, name: InfisicalResource.HttpEndpointName)
            .WithEnvironment("ENCRYPTION_KEY", settings.EncryptionKey)
            .WithEnvironment("AUTH_SECRET", settings.AuthSecret)
            .WithEnvironment("DB_CONNECTION_URI", dbConnectionUri)
            .WithEnvironment("REDIS_URL", redisUrl)
            .WithEnvironment("SITE_URL", settings.SiteUrl)
            .WithEnvironment("TELEMETRY_ENABLED", settings.TelemetryEnabled.ToString().ToLowerInvariant());

        return container;
    }

    /// <summary>
    /// Adds an Infisical container along with dedicated PostgreSQL and Redis dependencies
    /// using the proper Aspire resource types.
    /// Connection strings are derived from the created resources at runtime.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name for the Infisical container.</param>
    /// <param name="port">Optional host port to map to Infisical's internal port (8080).</param>
    /// <param name="imageTag">The Infisical Docker image tag. Defaults to <c>latest</c>.</param>
    /// <returns>
    /// A tuple containing resource builders for the Infisical container, the PostgreSQL database, and the Redis cache.
    /// </returns>
    public static (IResourceBuilder<InfisicalResource> infisical, IResourceBuilder<PostgresDatabaseResource> postgres, IResourceBuilder<RedisResource> cache) AddInfisicalWithDependencies(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "infisical",
        int? port = null,
        string imageTag = "latest")
    {
        ArgumentNullException.ThrowIfNull(builder);

        var postgres = builder.AddPostgres($"{name}-postgres")
            .AddDatabase($"{name}-db");

        var cache = builder.AddRedis($"{name}-cache");

        var infisical = ConfigureInfisical(builder, postgres, cache, name, port, imageTag);

        return (infisical, postgres, cache);
    }

    /// <summary>
    /// Adds an Infisical container along with dedicated PostgreSQL and Valkey dependencies
    /// using the proper Aspire resource types.
    /// Connection strings are derived from the created resources at runtime.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name for the Infisical container.</param>
    /// <param name="port">Optional host port to map to Infisical's internal port (8080).</param>
    /// <param name="imageTag">The Infisical Docker image tag. Defaults to <c>latest</c>.</param>
    /// <returns>
    /// A tuple containing resource builders for the Infisical container, the PostgreSQL database, and the Valkey cache.
    /// </returns>
    public static (IResourceBuilder<InfisicalResource> infisical, IResourceBuilder<PostgresDatabaseResource> postgres, IResourceBuilder<ValkeyResource> cache) AddInfisicalWithValkeyDependencies(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "infisical",
        int? port = null,
        string imageTag = "latest")
    {
        ArgumentNullException.ThrowIfNull(builder);

        var postgres = builder.AddPostgres($"{name}-postgres")
            .AddDatabase($"{name}-db");

        var cache = builder.AddValkey($"{name}-cache");

        var infisical = ConfigureInfisical(builder, postgres, cache, name, port, imageTag);

        return (infisical, postgres, cache);
    }

    /// <summary>
    /// Adds an Infisical container that uses existing PostgreSQL and Redis-compatible cache resources.
    /// Use this overload to share a single cache and/or database instance across multiple services
    /// instead of creating dedicated containers for Infisical.
    /// The cache resource can be any Redis-compatible server such as Redis or Valkey.
    /// Connection strings are resolved from the provided resources at runtime via Aspire's
    /// <see cref="IResourceWithConnectionString"/> mechanism.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="postgres">An existing PostgreSQL resource whose connection string will be used for <c>DB_CONNECTION_URI</c>.</param>
    /// <param name="cache">An existing Redis-compatible cache resource (Redis, Valkey, etc.) whose connection string will be used for <c>REDIS_URL</c>.</param>
    /// <param name="name">The resource name for the Infisical container.</param>
    /// <param name="port">Optional host port to map to Infisical's internal port (8080).</param>
    /// <param name="imageTag">The Infisical Docker image tag. Defaults to <c>latest</c>.</param>
    /// <returns>A resource builder for the Infisical container.</returns>
    public static IResourceBuilder<InfisicalResource> AddInfisicalUsingResources(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<IResourceWithConnectionString> postgres,
        IResourceBuilder<IResourceWithConnectionString> cache,
        [ResourceName] string name = "infisical",
        int? port = null,
        string imageTag = "latest")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(postgres);
        ArgumentNullException.ThrowIfNull(cache);

        return ConfigureInfisical(builder, postgres, cache, name, port, imageTag);
    }

    /// <summary>
    /// Injects Infisical client configuration into the target resource as environment variables.
    /// The injected values are automatically picked up by
    /// <c>Bielu.Aspire.Infisical.Client.AddInfisicalConfiguration</c> in the service project,
    /// eliminating the need for manual <c>appsettings.json</c> or callback configuration.
    /// <para>
    /// This also calls <see cref="ResourceBuilderExtensions.WithReference"/> to inject the
    /// Infisical connection string and <see cref="ResourceBuilderExtensions.WaitFor"/> to
    /// ensure the Infisical server is ready.
    /// </para>
    /// <para>
    /// Environment variables are injected using the <c>Infisical__Client__*</c> prefix, which
    /// the .NET configuration system automatically maps to <c>Infisical:Client:*</c>.
    /// </para>
    /// </summary>
    /// <typeparam name="T">A resource type that supports environment variables (e.g., a project or container).</typeparam>
    /// <param name="builder">The resource builder for the target service.</param>
    /// <param name="infisical">The Infisical resource to reference.</param>
    /// <param name="configureClient">
    /// A callback to configure the Infisical client settings that will be injected as environment variables.
    /// At minimum, <see cref="InfisicalClientConfiguration.ProjectId"/> and
    /// <see cref="InfisicalClientConfiguration.Environment"/> should be set, along with either
    /// machine identity credentials or a service token.
    /// </param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<T> WithInfisicalClient<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<InfisicalResource> infisical,
        Action<InfisicalClientConfiguration> configureClient)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(infisical);
        ArgumentNullException.ThrowIfNull(configureClient);

        var clientConfig = new InfisicalClientConfiguration();
        configureClient(clientConfig);

        builder = builder
            .WithReference(infisical)
            .WaitFor(infisical);

        if (!string.IsNullOrEmpty(clientConfig.ProjectId))
        {
            builder = builder.WithEnvironment("Infisical__Client__ProjectId", clientConfig.ProjectId);
        }

        if (!string.IsNullOrEmpty(clientConfig.Environment))
        {
            builder = builder.WithEnvironment("Infisical__Client__Environment", clientConfig.Environment);
        }

        if (!string.IsNullOrEmpty(clientConfig.SecretPath))
        {
            builder = builder.WithEnvironment("Infisical__Client__SecretPath", clientConfig.SecretPath);
        }

        if (!string.IsNullOrEmpty(clientConfig.ServiceToken))
        {
            builder = builder.WithEnvironment("Infisical__Client__ServiceToken", clientConfig.ServiceToken);
        }

        if (!string.IsNullOrEmpty(clientConfig.ClientId))
        {
            builder = builder.WithEnvironment("Infisical__Client__ClientId", clientConfig.ClientId);
        }

        if (!string.IsNullOrEmpty(clientConfig.ClientSecret))
        {
            builder = builder.WithEnvironment("Infisical__Client__ClientSecret", clientConfig.ClientSecret);
        }

        return builder;
    }

    private static IResourceBuilder<InfisicalResource> ConfigureInfisical(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<IResourceWithConnectionString> postgres,
        IResourceBuilder<IResourceWithConnectionString> cache,
        string name,
        int? port,
        string imageTag)
    {
        var settings = ReadInfisicalSettings(builder);

        var infisical = new InfisicalResource(name);

        var container = builder.AddResource(infisical)
            .WithImage("infisical/infisical", imageTag)
            .WithHttpEndpoint(port: port, targetPort: 8080, name: InfisicalResource.HttpEndpointName)
            .WithEnvironment("ENCRYPTION_KEY", settings.EncryptionKey)
            .WithEnvironment("AUTH_SECRET", settings.AuthSecret)
            .WithEnvironment("DB_CONNECTION_URI", postgres)
            .WithEnvironment("REDIS_URL", cache)
            .WithEnvironment("SITE_URL", settings.SiteUrl)
            .WithEnvironment("TELEMETRY_ENABLED", settings.TelemetryEnabled.ToString().ToLowerInvariant())
            .WaitFor(postgres)
            .WaitFor(cache);

        return container;
    }

    private sealed record InfisicalSettings(string EncryptionKey, string AuthSecret, string SiteUrl, bool TelemetryEnabled);

    private static InfisicalSettings ReadInfisicalSettings(IDistributedApplicationBuilder builder)
    {
        var infisicalConfig = builder.Configuration.GetSection("Infisical");

        var encryptionKey = infisicalConfig.GetValue<string>("EncryptionKey")
                            ?? throw new InvalidOperationException(
                                "Infisical:EncryptionKey configuration is required. " +
                                "Generate one with: openssl rand -hex 16");

        var authSecret = infisicalConfig.GetValue<string>("AuthSecret")
                         ?? throw new InvalidOperationException(
                             "Infisical:AuthSecret configuration is required. " +
                             "Generate one with: openssl rand -base64 32");

        var siteUrl = infisicalConfig.GetValue<string>("SiteUrl") ?? "http://localhost:8080";
        var telemetryEnabled = infisicalConfig.GetValue<bool?>("TelemetryEnabled") ?? false;

        return new InfisicalSettings(encryptionKey, authSecret, siteUrl, telemetryEnabled);
    }
}
