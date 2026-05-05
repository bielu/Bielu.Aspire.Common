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
        BindClientConfiguration(builder, infisical);

        var resourceBuilder = builder.AddResource(infisical)
            .WithImage("infisical/infisical", imageTag)
            .WithHttpEndpoint(port: port, targetPort: 8080, name: InfisicalResource.HttpEndpointName)
            .WithEnvironment("ENCRYPTION_KEY", settings.EncryptionKey)
            .WithEnvironment("AUTH_SECRET", settings.AuthSecret)
            .WithEnvironment("DB_CONNECTION_URI", dbConnectionUri)
            .WithEnvironment("REDIS_URL", redisUrl)
            .WithEnvironment("TELEMETRY_ENABLED", settings.TelemetryEnabled.ToString().ToLowerInvariant());

        if (!string.IsNullOrEmpty(settings.SiteUrl))
        {
            resourceBuilder = resourceBuilder.WithEnvironment("SITE_URL", settings.SiteUrl);
        }

        return resourceBuilder;
    }

    /// <summary>
    /// Sets the Infisical <c>SITE_URL</c> environment variable to an explicit, stable URL.
    /// <para>
    /// This must be a URL that is reachable both from end users (their browser, for email
    /// links and OAuth redirects) and from inside the Infisical container itself (Infisical
    /// reads <c>SITE_URL</c> at boot during DB migrations). Aspire's proxied dashboard
    /// endpoint is NOT suitable for this — use the host-mapped port you exposed on the
    /// Infisical container (e.g. <c>http://localhost:8080</c>) or a real public URL.
    /// </para>
    /// </summary>
    /// <param name="builder">The Infisical resource builder.</param>
    /// <param name="siteUrl">The absolute site URL, e.g. <c>http://localhost:8080</c>.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<InfisicalResource> WithSiteUrl(
        this IResourceBuilder<InfisicalResource> builder,
        string siteUrl)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(siteUrl);

        return builder.WithEnvironment("SITE_URL", siteUrl);
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
        int? port = 8080,
        string imageTag = "latest")
    {
        ArgumentNullException.ThrowIfNull(builder);

        var postgres = builder.AddPostgres($"{name}-postgres")
            .AddDatabase($"{name}-db");

        var cache = builder.AddRedis($"{name}-cache");

        var dbUri = postgres.Resource.UriExpression;
        var redisUri = cache.Resource.UriExpression;

        var infisical = ConfigureInfisical(builder, postgres, cache, dbUri, redisUri, name, port, imageTag);

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

        var dbUri = postgres.Resource.UriExpression;
        var redisUri = cache.Resource.UriExpression;

        var infisical = ConfigureInfisical(builder, postgres, cache, dbUri, redisUri, name, port, imageTag);

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
        IResourceBuilder<PostgresDatabaseResource> postgres,
        IResourceBuilder<IResourceWithConnectionString> cache,
        [ResourceName] string name = "infisical",
        int? port = null,
        string imageTag = "latest")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(postgres);
        ArgumentNullException.ThrowIfNull(cache);

        var dbUri = postgres.Resource.UriExpression;
        var redisUri = cache.Resource switch
        {
            RedisResource redis => redis.UriExpression,
            ValkeyResource valkey => valkey.UriExpression,
            _ => throw new InvalidOperationException(
                $"Unsupported cache resource type '{cache.Resource.GetType().Name}'. " +
                "Infisical requires a redis:// URI; pass a RedisResource or ValkeyResource.")
        };

        return ConfigureInfisical(builder, postgres, cache, dbUri, redisUri, name, port, imageTag);
    }

    /// <summary>
    /// Configures the client settings stored on the <see cref="InfisicalResource"/>.
    /// These settings are automatically propagated to consuming service projects when
    /// <see cref="WithInfisicalClient{T}(IResourceBuilder{T}, IResourceBuilder{InfisicalResource}, Action{InfisicalClientConfiguration}?)"/>
    /// is called.
    /// <para>
    /// Values set here override any values that were automatically read from the
    /// <c>Infisical:Client</c> configuration section at resource creation time.
    /// </para>
    /// </summary>
    /// <param name="builder">The Infisical resource builder.</param>
    /// <param name="configureClient">
    /// A callback to configure the client settings on the resource.
    /// </param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<InfisicalResource> WithClientConfiguration(
        this IResourceBuilder<InfisicalResource> builder,
        Action<InfisicalClientConfiguration> configureClient)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureClient);

        configureClient(builder.Resource.ClientConfiguration);

        return builder;
    }

    /// <summary>
    /// Injects Infisical client configuration into the target resource as environment variables.
    /// The injected values are automatically picked up by
    /// <c>Bielu.Aspire.Infisical.Client.AddInfisicalConfiguration</c> in the service project,
    /// eliminating the need for manual <c>appsettings.json</c> or callback configuration.
    /// <para>
    /// Client settings (ProjectId, Environment, ClientId, ClientSecret, etc.) are read from the
    /// <see cref="InfisicalResource.ClientConfiguration"/> on the Infisical resource. These are
    /// populated automatically from the <c>Infisical:Client</c> AppHost configuration section
    /// and can be overridden via <see cref="WithClientConfiguration"/>.
    /// </para>
    /// <para>
    /// Sensitive values (<c>ClientSecret</c> and <c>ServiceToken</c>) are injected as
    /// secret <see cref="Aspire.Hosting.ApplicationModel.ParameterResource"/> instances
    /// (created with <c>secret: true</c>) so they are masked in the Aspire dashboard and logs
    /// rather than being exposed as plain-text environment variables.
    /// </para>
    /// <para>
    /// This also calls <see cref="ResourceBuilderExtensions.WithReference"/> to inject the
    /// Infisical connection string and <see cref="ResourceBuilderExtensions.WaitFor"/> to
    /// ensure the Infisical server is ready.
    /// </para>
    /// <para>
    /// Non-sensitive environment variables are injected using the <c>Infisical__Client__*</c> prefix,
    /// which the .NET configuration system automatically maps to <c>Infisical:Client:*</c>.
    /// </para>
    /// </summary>
    /// <typeparam name="T">A resource type that supports environment variables and wait (e.g., a project).</typeparam>
    /// <param name="builder">The resource builder for the target service.</param>
    /// <param name="infisical">The Infisical resource whose client configuration will be injected.</param>
    /// <param name="configureClient">
    /// An optional callback to override specific client settings for this particular service.
    /// Values set here take precedence over the resource-level configuration.
    /// </param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<T> WithInfisicalClient<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<InfisicalResource> infisical,
        Action<InfisicalClientConfiguration>? configureClient = null)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(infisical);

        // Start from the resource-level configuration.
        var resourceConfig = infisical.Resource.ClientConfiguration;
        var clientConfig = new InfisicalClientConfiguration
        {
            ProjectId = resourceConfig.ProjectId,
            Environment = resourceConfig.Environment,
            SecretPath = resourceConfig.SecretPath,
            ClientId = resourceConfig.ClientId,
            ClientSecret = resourceConfig.ClientSecret,
            ServiceToken = resourceConfig.ServiceToken,
            SslProjectId = resourceConfig.SslProjectId,
            SslEnvironment = resourceConfig.SslEnvironment,
            SslSecretPath = resourceConfig.SslSecretPath
        };

        // Allow per-service overrides.
        configureClient?.Invoke(clientConfig);

        builder = builder
            .WithReference(infisical)
            .WaitFor(infisical);

        var isPublishMode = infisical.ApplicationBuilder.ExecutionContext.IsPublishMode;
        var resourceName = infisical.Resource.Name;

        // In publish mode, all values are exposed as Aspire parameters (so they appear in the
        // generated manifest and can be supplied at deploy time) instead of being baked in as
        // literal environment variables. In run mode, literals are used directly for convenience.
        builder = ApplyClientEnv(builder, infisical, isPublishMode,
            "Infisical__Client__ProjectId", $"{resourceName}-infisical-client-project-id",
            clientConfig.ProjectId, secret: false);

        builder = ApplyClientEnv(builder, infisical, isPublishMode,
            "Infisical__Client__Environment", $"{resourceName}-infisical-client-environment",
            clientConfig.Environment, secret: false);

        builder = ApplyClientEnv(builder, infisical, isPublishMode,
            "Infisical__Client__SecretPath", $"{resourceName}-infisical-client-secret-path",
            clientConfig.SecretPath, secret: false);

        builder = ApplyClientEnv(builder, infisical, isPublishMode,
            "Infisical__Client__ServiceToken", $"{resourceName}-infisical-client-service-token",
            clientConfig.ServiceToken, secret: true);

        builder = ApplyClientEnv(builder, infisical, isPublishMode,
            "Infisical__Client__ClientId", $"{resourceName}-infisical-client-id",
            clientConfig.ClientId, secret: false);

        builder = ApplyClientEnv(builder, infisical, isPublishMode,
            "Infisical__Client__ClientSecret", $"{resourceName}-infisical-client-secret",
            clientConfig.ClientSecret, secret: true);

        builder = ApplyClientEnv(builder, infisical, isPublishMode,
            "Infisical__Client__SslProjectId", $"{resourceName}-infisical-client-ssl-project-id",
            clientConfig.SslProjectId, secret: false);

        builder = ApplyClientEnv(builder, infisical, isPublishMode,
            "Infisical__Client__SslEnvironment", $"{resourceName}-infisical-client-ssl-environment",
            clientConfig.SslEnvironment, secret: false);

        builder = ApplyClientEnv(builder, infisical, isPublishMode,
            "Infisical__Client__SslSecretPath", $"{resourceName}-infisical-client-ssl-secret-path",
            clientConfig.SslSecretPath, secret: false);

        return builder;
    }

    private static IResourceBuilder<T> ApplyClientEnv<T>(
        IResourceBuilder<T> builder,
        IResourceBuilder<InfisicalResource> infisical,
        bool isPublishMode,
        string envName,
        string parameterName,
        string? value,
        bool secret)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        if (isPublishMode)
        {
            // In publish mode, always expose as a parameter so the value is supplied at deploy time
            // rather than being captured from the AppHost configuration.
            var parameter = secret
                ? infisical.ApplicationBuilder.AddParameter(parameterName, secret: true)
                : infisical.ApplicationBuilder.AddParameter(parameterName);
            return builder.WithEnvironment(envName, parameter);
        }

        if (string.IsNullOrEmpty(value))
        {
            return builder;
        }

        if (secret)
        {
            var parameter = infisical.ApplicationBuilder.AddParameter(parameterName, value, secret: true);
            return builder.WithEnvironment(envName, parameter);
        }

        return builder.WithEnvironment(envName, value);
    }

    /// <summary>
    /// Configures SMTP settings on the Infisical container. Infisical requires SMTP to send
    /// invitation emails, password resets, and email verification codes.
    /// <para>
    /// Sensitive values (<c>SMTP_PASSWORD</c>) are injected as secret
    /// <see cref="ParameterResource"/> instances so they are masked in the Aspire dashboard
    /// and logs rather than being exposed as plain-text environment variables.
    /// </para>
    /// </summary>
    /// <param name="builder">The Infisical resource builder.</param>
    /// <param name="host">SMTP server host (e.g., <c>smtp.gmail.com</c>).</param>
    /// <param name="port">SMTP server port (e.g., <c>587</c>).</param>
    /// <param name="username">SMTP username for authentication.</param>
    /// <param name="password">SMTP password for authentication. Will be stored as a secret parameter.</param>
    /// <param name="fromAddress">The <c>from</c> address used when sending emails.</param>
    /// <param name="fromName">The <c>from</c> display name used when sending emails. Defaults to <c>Infisical</c>.</param>
    /// <param name="secure">If true, sets <c>SMTP_SECURE=true</c> (use TLS/SSL).</param>
    /// <param name="requireTls">If true, sets <c>SMTP_REQUIRE_TLS=true</c>.</param>
    /// <param name="ignoreTls">If true, sets <c>SMTP_IGNORE_TLS=true</c>.</param>
    /// <param name="tlsRejectUnauthorized">
    /// If <c>false</c>, Infisical will accept the SMTP server's TLS certificate even if it
    /// is self-signed or otherwise untrusted (sets <c>SMTP_TLS_REJECT_UNAUTHORIZED=false</c>).
    /// Useful for local development against self-signed dev SMTP servers (e.g. <c>smtp4dev</c>).
    /// Do NOT use in production.
    /// </param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<InfisicalResource> WithSmtp(
        this IResourceBuilder<InfisicalResource> builder,
        string host,
        int port,
        string? username,
        string? password,
        string fromAddress,
        string fromName = "Infisical",
        bool? secure = null,
        bool? requireTls = null,
        bool? ignoreTls = null,
        bool? tlsRejectUnauthorized = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentException.ThrowIfNullOrEmpty(fromAddress);

        builder = builder
            .WithEnvironment("SMTP_HOST", host)
            .WithEnvironment("SMTP_PORT", port.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithEnvironment("SMTP_FROM_ADDRESS", fromAddress)
            .WithEnvironment("SMTP_FROM_NAME", fromName);

        if (!string.IsNullOrEmpty(username))
        {
            builder = builder.WithEnvironment("SMTP_USERNAME", username);
        }

        if (!string.IsNullOrEmpty(password))
        {
            var passwordParam = builder.ApplicationBuilder.AddParameter(
                $"{builder.Resource.Name}-smtp-password",
                password,
                secret: true);
            builder = builder.WithEnvironment("SMTP_PASSWORD", passwordParam);
        }

        if (secure.HasValue)
        {
            builder = builder.WithEnvironment("SMTP_SECURE", secure.Value.ToString().ToLowerInvariant());
        }

        if (requireTls.HasValue)
        {
            builder = builder.WithEnvironment("SMTP_REQUIRE_TLS", requireTls.Value.ToString().ToLowerInvariant());
        }

        if (ignoreTls.HasValue)
        {
            builder = builder.WithEnvironment("SMTP_IGNORE_TLS", ignoreTls.Value.ToString().ToLowerInvariant());
        }

        if (tlsRejectUnauthorized.HasValue)
        {
            builder = builder.WithEnvironment("SMTP_TLS_REJECT_UNAUTHORIZED", tlsRejectUnauthorized.Value.ToString().ToLowerInvariant());
        }

        return builder;
    }

    /// <summary>
    /// Configures SMTP settings on the Infisical container by reading values from the
    /// <c>Infisical:Smtp</c> AppHost configuration section.
    /// <para>
    /// Required keys: <c>Host</c>, <c>Port</c>, <c>FromAddress</c>.
    /// Optional keys: <c>Username</c>, <c>Password</c>, <c>FromName</c> (defaults to <c>Infisical</c>),
    /// <c>Secure</c>, <c>RequireTls</c>, <c>IgnoreTls</c>.
    /// </para>
    /// </summary>
    /// <param name="builder">The Infisical resource builder.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static IResourceBuilder<InfisicalResource> WithSmtpFromConfiguration(
        this IResourceBuilder<InfisicalResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var smtp = builder.ApplicationBuilder.Configuration.GetSection("Infisical:Smtp");
        if (!smtp.Exists())
        {
            throw new InvalidOperationException(
                "Infisical:Smtp configuration section is required when calling WithSmtpFromConfiguration.");
        }

        var host = smtp.GetValue<string>("Host")
                   ?? throw new InvalidOperationException("Infisical:Smtp:Host configuration is required.");
        var port = smtp.GetValue<int?>("Port")
                   ?? throw new InvalidOperationException("Infisical:Smtp:Port configuration is required.");
        var username = smtp.GetValue<string>("Username");
        var password = smtp.GetValue<string>("Password");
        var fromAddress = smtp.GetValue<string>("FromAddress")
                          ?? throw new InvalidOperationException("Infisical:Smtp:FromAddress configuration is required.");
        var fromName = smtp.GetValue<string>("FromName") ?? "Infisical";
        var secure = smtp.GetValue<bool?>("Secure");
        var requireTls = smtp.GetValue<bool?>("RequireTls");
        var ignoreTls = smtp.GetValue<bool?>("IgnoreTls");
        var tlsRejectUnauthorized = smtp.GetValue<bool?>("TlsRejectUnauthorized");

        return builder.WithSmtp(host, port, username, password, fromAddress, fromName, secure, requireTls, ignoreTls, tlsRejectUnauthorized);
    }

    private static IResourceBuilder<InfisicalResource> ConfigureInfisical(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<IResource> postgres,
        IResourceBuilder<IResource> cache,
        ReferenceExpression dbUri,
        ReferenceExpression redisUri,
        string name,
        int? port,
        string imageTag)
    {
        var settings = ReadInfisicalSettings(builder);

        var infisical = new InfisicalResource(name);
        BindClientConfiguration(builder, infisical);

        // Aspire's default Postgres / Redis containers do not enable TLS, but Infisical's
        // database client (knex/pg) and ioredis may attempt to negotiate TLS depending on
        // the URI / runtime. To avoid `self-signed certificate` / `DEPTH_ZERO_SELF_SIGNED_CERT`
        // and similar errors against the local dev containers we explicitly:
        //   - append `sslmode=disable` to the Postgres URI because the Aspire Postgres
        //     container does not enable TLS at all; node-postgres would otherwise attempt
        //     SSL and fail with "The server does not support SSL connections". Using
        //     `disable` (instead of `no-verify`) ensures the client doesn't try TLS.
        //   - set `NODE_TLS_REJECT_UNAUTHORIZED=0` as a belt-and-suspenders fallback for any
        //     other outbound TLS Infisical performs against self-signed dev services
        //     (Redis/Valkey, SMTP). NEVER use this in production.
        var dbUriWithSsl = ReferenceExpression.Create($"{dbUri}?sslmode=disable");

        var resourceBuilder = builder.AddResource(infisical)
            .WithImage("infisical/infisical", imageTag)
            .WithHttpEndpoint(port: port, targetPort: 8080, name: InfisicalResource.HttpEndpointName)
            .WithEnvironment("ENCRYPTION_KEY", settings.EncryptionKey)
            .WithEnvironment("AUTH_SECRET", settings.AuthSecret)
            .WithEnvironment(ctx => ctx.EnvironmentVariables["DB_CONNECTION_URI"] = dbUriWithSsl)
            .WithEnvironment(ctx => ctx.EnvironmentVariables["REDIS_URL"] = redisUri)
            .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
            .WithEnvironment("TELEMETRY_ENABLED", settings.TelemetryEnabled.ToString().ToLowerInvariant());

        // SITE_URL must be a stable URL that the user's browser can reach AND that Infisical
        // itself can use to build links in emails / OAuth callbacks. Aspire's proxied endpoint
        // (the one exposed via the dashboard) cannot be used here because:
        //   1. It is not known until the AppHost wires up endpoints, and Infisical reads
        //      SITE_URL once at boot to run DB migrations - using a ReferenceExpression that
        //      depends on the Infisical resource's own endpoint causes a circular reference.
        //   2. The proxied host (e.g. localhost:randomPort) is not reachable from inside the
        //      Infisical container when it tries to resolve its own SITE_URL.
        // Therefore we only honor an explicit Infisical:SiteUrl from configuration; if not set,
        // we fall back to a sensible default that the user is expected to override via
        // WithSiteUrl(...) once they know the host port they want to expose.
        if (!string.IsNullOrEmpty(settings.SiteUrl))
        {
            resourceBuilder = resourceBuilder.WithEnvironment("SITE_URL", settings.SiteUrl);
        }
        else
        {
            resourceBuilder = resourceBuilder.WithEnvironment("SITE_URL", "http://localhost:8080");

        }

        if (postgres is IResourceBuilder<IResourceWithWaitSupport> pgWait)
        {
            resourceBuilder = resourceBuilder.WaitFor(pgWait);
        }

        if (cache is IResourceBuilder<IResourceWithWaitSupport> cacheWait)
        {
            resourceBuilder = resourceBuilder.WaitFor(cacheWait);
        }

        return resourceBuilder;
    }

    /// <summary>
    /// Reads client configuration from the <c>Infisical:Client</c> AppHost configuration section
    /// and stores it on the <see cref="InfisicalResource.ClientConfiguration"/>.
    /// </summary>
    private static void BindClientConfiguration(IDistributedApplicationBuilder builder, InfisicalResource resource)
    {
        var clientSection = builder.Configuration.GetSection("Infisical:Client");
        if (!clientSection.Exists())
        {
            return;
        }

        var config = resource.ClientConfiguration;
        config.ProjectId = clientSection.GetValue<string>("ProjectId") ?? config.ProjectId;
        config.Environment = clientSection.GetValue<string>("Environment") ?? config.Environment;
        config.SecretPath = clientSection.GetValue<string>("SecretPath") ?? config.SecretPath;
        config.ClientId = clientSection.GetValue<string>("ClientId") ?? config.ClientId;
        config.ClientSecret = clientSection.GetValue<string>("ClientSecret") ?? config.ClientSecret;
        config.ServiceToken = clientSection.GetValue<string>("ServiceToken") ?? config.ServiceToken;
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

        // Note: SiteUrl is intentionally optional and has no default. Aspire's proxied
        // endpoint cannot be used as Infisical's SITE_URL (see ConfigureInfisical for details),
        // so we leave it empty unless the user explicitly configures it via Infisical:SiteUrl
        // or WithSiteUrl(...). Infisical itself will fall back to its own default when unset.
        var siteUrl = infisicalConfig.GetValue<string>("SiteUrl") ?? string.Empty;
        var telemetryEnabled = infisicalConfig.GetValue<bool?>("TelemetryEnabled") ?? false;

        return new InfisicalSettings(encryptionKey, authSecret, siteUrl, telemetryEnabled);
    }
}
