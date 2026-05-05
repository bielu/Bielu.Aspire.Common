namespace Bielu.Aspire.Infisical;

/// <summary>
/// Configuration values for Infisical client settings that are injected as environment
/// variables into consuming service projects via
/// <see cref="BuilderExtensions.WithInfisicalClient{T}"/>.
/// <para>
/// These values map to the <c>Infisical:Client:*</c> configuration section and are
/// automatically picked up by <c>Bielu.Aspire.Infisical.Client.AddInfisicalConfiguration</c>.
/// </para>
/// </summary>
public sealed class InfisicalClientConfiguration
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
}
