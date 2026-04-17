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
}
