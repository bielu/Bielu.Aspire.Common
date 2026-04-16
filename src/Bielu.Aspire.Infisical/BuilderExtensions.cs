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
    public static IResourceBuilder<ContainerResource> AddInfisical(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "infisical",
        int? port = null,
        string imageTag = "latest")
    {
        ArgumentNullException.ThrowIfNull(builder);

        var infisicalConfig = builder.Configuration.GetSection("Infisical");

        var encryptionKey = infisicalConfig.GetValue<string>("EncryptionKey")
                            ?? throw new InvalidOperationException(
                                "Infisical:EncryptionKey configuration is required. " +
                                "Generate one with: openssl rand -hex 16");

        var authSecret = infisicalConfig.GetValue<string>("AuthSecret")
                         ?? throw new InvalidOperationException(
                             "Infisical:AuthSecret configuration is required. " +
                             "Generate one with: openssl rand -base64 32");

        var dbConnectionUri = infisicalConfig.GetValue<string>("DbConnectionUri")
                              ?? throw new InvalidOperationException(
                                  "Infisical:DbConnectionUri configuration is required. " +
                                  "Example: postgresql://user:password@host:5432/infisical");

        var redisUrl = infisicalConfig.GetValue<string>("RedisUrl")
                       ?? throw new InvalidOperationException(
                           "Infisical:RedisUrl configuration is required. " +
                           "Example: redis://host:6379");

        var siteUrl = infisicalConfig.GetValue<string>("SiteUrl") ?? "http://localhost:8080";
        var telemetryEnabled = infisicalConfig.GetValue<bool?>("TelemetryEnabled") ?? false;

        var container = builder.AddContainer(name, "infisical/infisical", imageTag)
            .WithHttpEndpoint(port: port, targetPort: 8080, name: "http")
            .WithEnvironment("ENCRYPTION_KEY", encryptionKey)
            .WithEnvironment("AUTH_SECRET", authSecret)
            .WithEnvironment("DB_CONNECTION_URI", dbConnectionUri)
            .WithEnvironment("REDIS_URL", redisUrl)
            .WithEnvironment("SITE_URL", siteUrl)
            .WithEnvironment("TELEMETRY_ENABLED", telemetryEnabled.ToString().ToLowerInvariant());

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
    public static (IResourceBuilder<ContainerResource> infisical, IResourceBuilder<PostgresDatabaseResource> postgres, IResourceBuilder<RedisResource> cache) AddInfisicalWithDependencies(
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
    public static (IResourceBuilder<ContainerResource> infisical, IResourceBuilder<PostgresDatabaseResource> postgres, IResourceBuilder<ValkeyResource> cache) AddInfisicalWithValkeyDependencies(
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
    public static IResourceBuilder<ContainerResource> AddInfisicalWithDependencies(
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

    private static IResourceBuilder<ContainerResource> ConfigureInfisical(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<IResourceWithConnectionString> postgres,
        IResourceBuilder<IResourceWithConnectionString> cache,
        string name,
        int? port,
        string imageTag)
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

        var infisical = builder.AddContainer(name, "infisical/infisical", imageTag)
            .WithHttpEndpoint(port: port, targetPort: 8080, name: "http")
            .WithEnvironment("ENCRYPTION_KEY", encryptionKey)
            .WithEnvironment("AUTH_SECRET", authSecret)
            .WithEnvironment("DB_CONNECTION_URI", postgres)
            .WithEnvironment("REDIS_URL", cache)
            .WithEnvironment("SITE_URL", siteUrl)
            .WithEnvironment("TELEMETRY_ENABLED", telemetryEnabled.ToString().ToLowerInvariant())
            .WaitFor(postgres)
            .WaitFor(cache);

        return infisical;
    }
}
