namespace Bielu.Aspire.Infisical.Client;

/// <summary>
/// Configuration settings for connecting to an Infisical secrets management server.
/// These settings are typically populated from the <c>Infisical:Client</c> configuration section
/// or via the <see cref="InfisicalClientBuilderExtensions.AddInfisicalConfiguration"/> options callback.
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
    /// Gets or sets the secret path within the project. Defaults to <c>/</c>.
    /// </summary>
    public string SecretPath { get; set; } = "/";

    /// <summary>
    /// Gets or sets an optional prefix to apply to all secret keys.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// Gets or sets the client ID for Universal Auth machine identity authentication.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret for Universal Auth machine identity authentication.
    /// </summary>
    public string? ClientSecret { get; set; }
}
