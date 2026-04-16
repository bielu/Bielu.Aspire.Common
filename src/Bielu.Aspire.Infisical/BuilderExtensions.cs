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
    /// Adds an Infisical container along with its PostgreSQL and Redis-compatible cache dependencies.
    /// This is a convenience method that creates all three containers.
    /// Infisical only supports PostgreSQL as its database engine.
    /// The cache engine can be any Redis-compatible server such as Redis or Valkey.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name for the Infisical container.</param>
    /// <param name="port">Optional host port to map to Infisical's internal port (8080).</param>
    /// <param name="imageTag">The Infisical Docker image tag. Defaults to <c>latest</c>.</param>
    /// <param name="postgresImageTag">The PostgreSQL Docker image tag. Defaults to <c>14-alpine</c>.</param>
    /// <param name="cacheImage">The Docker image for the Redis-compatible cache engine (e.g. <c>redis</c>, <c>valkey/valkey</c>). Defaults to <c>redis</c>.</param>
    /// <param name="cacheImageTag">The Docker image tag for the cache engine. Defaults to <c>7-alpine</c>.</param>
    /// <returns>
    /// A tuple containing resource builders for the Infisical, PostgreSQL, and cache containers.
    /// </returns>
    public static (IResourceBuilder<ContainerResource> infisical, IResourceBuilder<ContainerResource> postgres, IResourceBuilder<ContainerResource> cache) AddInfisicalWithDependencies(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name = "infisical",
        int? port = null,
        string imageTag = "latest",
        string postgresImageTag = "14-alpine",
        string cacheImage = "redis",
        string cacheImageTag = "7-alpine")
    {
        ArgumentNullException.ThrowIfNull(builder);

        var infisicalConfig = builder.Configuration.GetSection("Infisical");

        var dbUser = infisicalConfig.GetValue<string>("Postgres:User") ?? "infisical";
        var dbPassword = infisicalConfig.GetValue<string>("Postgres:Password") ?? "infisical";
        var dbName = infisicalConfig.GetValue<string>("Postgres:Database") ?? "infisical";

        var postgres = builder.AddContainer($"{name}-postgres", "postgres", postgresImageTag)
            .WithEnvironment("POSTGRES_USER", dbUser)
            .WithEnvironment("POSTGRES_PASSWORD", dbPassword)
            .WithEnvironment("POSTGRES_DB", dbName)
            .WithVolume($"{name}-postgres-data", "/var/lib/postgresql/data");

        var cache = builder.AddContainer($"{name}-cache", cacheImage, cacheImageTag)
            .WithVolume($"{name}-cache-data", "/data");

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

        var dbConnectionUri = $"postgresql://{dbUser}:{dbPassword}@{name}-postgres:5432/{dbName}";
        var redisUrl = $"redis://{name}-cache:6379";

        var infisical = builder.AddContainer(name, "infisical/infisical", imageTag)
            .WithHttpEndpoint(port: port, targetPort: 8080, name: "http")
            .WithEnvironment("ENCRYPTION_KEY", encryptionKey)
            .WithEnvironment("AUTH_SECRET", authSecret)
            .WithEnvironment("DB_CONNECTION_URI", dbConnectionUri)
            .WithEnvironment("REDIS_URL", redisUrl)
            .WithEnvironment("SITE_URL", siteUrl)
            .WithEnvironment("TELEMETRY_ENABLED", telemetryEnabled.ToString().ToLowerInvariant())
            .WaitFor(postgres)
            .WaitFor(cache);

        return (infisical, postgres, cache);
    }

    /// <summary>
    /// Adds an Infisical container that uses existing PostgreSQL and Redis-compatible cache resources.
    /// Use this overload to share a single cache and/or database instance across multiple services
    /// instead of creating dedicated containers for Infisical.
    /// The cache resource can be any Redis-compatible server such as Redis or Valkey.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="postgres">An existing PostgreSQL resource to use.</param>
    /// <param name="cache">An existing Redis-compatible cache resource (Redis, Valkey, etc.) to use.</param>
    /// <param name="name">The resource name for the Infisical container.</param>
    /// <param name="port">Optional host port to map to Infisical's internal port (8080).</param>
    /// <param name="imageTag">The Infisical Docker image tag. Defaults to <c>latest</c>.</param>
    /// <param name="cachePort">The port the cache resource listens on. Defaults to <c>6379</c>.</param>
    /// <returns>A resource builder for the Infisical container.</returns>
    public static IResourceBuilder<ContainerResource> AddInfisicalWithDependencies(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<IResource> postgres,
        IResourceBuilder<IResource> cache,
        [ResourceName] string name = "infisical",
        int? port = null,
        string imageTag = "latest",
        int cachePort = 6379)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(postgres);
        ArgumentNullException.ThrowIfNull(cache);

        var infisicalConfig = builder.Configuration.GetSection("Infisical");

        var encryptionKey = infisicalConfig.GetValue<string>("EncryptionKey")
                            ?? throw new InvalidOperationException(
                                "Infisical:EncryptionKey configuration is required. " +
                                "Generate one with: openssl rand -hex 16");

        var authSecret = infisicalConfig.GetValue<string>("AuthSecret")
                         ?? throw new InvalidOperationException(
                             "Infisical:AuthSecret configuration is required. " +
                             "Generate one with: openssl rand -base64 32");

        var dbUser = infisicalConfig.GetValue<string>("Postgres:User") ?? "infisical";
        var dbPassword = infisicalConfig.GetValue<string>("Postgres:Password") ?? "infisical";
        var dbName = infisicalConfig.GetValue<string>("Postgres:Database") ?? "infisical";

        var siteUrl = infisicalConfig.GetValue<string>("SiteUrl") ?? "http://localhost:8080";
        var telemetryEnabled = infisicalConfig.GetValue<bool?>("TelemetryEnabled") ?? false;

        var dbConnectionUri = $"postgresql://{dbUser}:{dbPassword}@{postgres.Resource.Name}:5432/{dbName}";
        var redisUrl = $"redis://{cache.Resource.Name}:{cachePort}";

        var infisical = builder.AddContainer(name, "infisical/infisical", imageTag)
            .WithHttpEndpoint(port: port, targetPort: 8080, name: "http")
            .WithEnvironment("ENCRYPTION_KEY", encryptionKey)
            .WithEnvironment("AUTH_SECRET", authSecret)
            .WithEnvironment("DB_CONNECTION_URI", dbConnectionUri)
            .WithEnvironment("REDIS_URL", redisUrl)
            .WithEnvironment("SITE_URL", siteUrl)
            .WithEnvironment("TELEMETRY_ENABLED", telemetryEnabled.ToString().ToLowerInvariant())
            .WaitFor(postgres)
            .WaitFor(cache);

        return infisical;
    }
}
