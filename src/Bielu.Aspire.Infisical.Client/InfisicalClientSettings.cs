namespace Bielu.Aspire.Infisical.Client;

/// <summary>
/// Configuration settings for connecting to an Infisical secrets management server.
/// These settings are typically populated from the <c>Infisical:Client</c> configuration section
/// and can be further overridden via the
/// <see cref="InfisicalClientBuilderExtensions.AddInfisicalConfiguration"/> options callback.
/// <para>
/// Properties set here take precedence over the values already bound by
/// <see cref="JJConsulting.Infisical.Configuration.MachineIdentityInfisicalConfig.FromConfiguration"/> or
/// <see cref="JJConsulting.Infisical.Configuration.ServiceTokenInfisicalConfig.FromConfiguration"/>.
/// </para>
/// </summary>
public sealed class InfisicalClientSettings
{
    /// <summary>
    /// Gets or sets the Infisical project ID to fetch secrets from.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the environment slug (e.g., <c>dev</c>, <c>staging</c>, <c>prod</c>).
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Gets or sets the secret path within the project.
    /// When <c>null</c>, the library default (<c>/</c>) is used.
    /// </summary>
    public string? SecretPath { get; set; }

    /// <summary>
    /// Gets or sets the client ID for Universal Auth machine identity authentication.
    /// Mutually exclusive with <see cref="ServiceToken"/>.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret for Universal Auth machine identity authentication.
    /// Mutually exclusive with <see cref="ServiceToken"/>.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the Infisical service token for service-token authentication.
    /// When set, <see cref="ClientId"/> and <see cref="ClientSecret"/> are ignored.
    /// </summary>
    public string? ServiceToken { get; set; }

    /// <summary>
    /// Gets or sets the Infisical project ID that holds TLS/SSL certificate secrets,
    /// when those are stored in a different project than the application secrets.
    /// When <c>null</c>, <see cref="ProjectId"/> is used as a fallback.
    /// </summary>
    public string? SslProjectId { get; set; }

    /// <summary>
    /// Gets or sets the environment slug used when fetching TLS/SSL certificate secrets.
    /// When <c>null</c>, <see cref="Environment"/> is used as a fallback.
    /// </summary>
    public string? SslEnvironment { get; set; }

    /// <summary>
    /// Gets or sets the secret path used when fetching TLS/SSL certificate secrets.
    /// When <c>null</c>, <see cref="SecretPath"/> is used as a fallback.
    /// </summary>
    public string? SslSecretPath { get; set; }

    /// <summary>
    /// Gets the effective project ID for fetching TLS/SSL certificate secrets,
    /// falling back to <see cref="ProjectId"/> when <see cref="SslProjectId"/> is not set.
    /// </summary>
    public string? EffectiveSslProjectId => string.IsNullOrEmpty(SslProjectId) ? ProjectId : SslProjectId;

    /// <summary>
    /// Gets the effective environment slug for fetching TLS/SSL certificate secrets,
    /// falling back to <see cref="Environment"/> when <see cref="SslEnvironment"/> is not set.
    /// </summary>
    public string? EffectiveSslEnvironment => string.IsNullOrEmpty(SslEnvironment) ? Environment : SslEnvironment;

    /// <summary>
    /// Gets the effective secret path for fetching TLS/SSL certificate secrets,
    /// falling back to <see cref="SecretPath"/> when <see cref="SslSecretPath"/> is not set.
    /// </summary>
    public string? EffectiveSslSecretPath => string.IsNullOrEmpty(SslSecretPath) ? SecretPath : SslSecretPath;
}
