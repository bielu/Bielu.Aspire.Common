using System.Security.Cryptography.X509Certificates;
using Infisical.Sdk;
using Infisical.Sdk.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace Bielu.Aspire.Infisical.Client;

/// <summary>
/// Provides Kestrel <see cref="ListenOptions"/> extension methods that load TLS certificates
/// directly from Infisical secrets, mirroring the ergonomics of the built-in
/// <c>listenOptions.UseHttps("cert.pfx", "password")</c> overload.
/// </summary>
public static class InfisicalKestrelExtensions
{
    /// <summary>
    /// Configures Kestrel to use HTTPS with a PFX certificate fetched from Infisical.
    /// <para>
    /// The secret named <paramref name="pfxSecretName"/> must contain the PFX file encoded
    /// as Base64. The secret named <paramref name="passwordSecretName"/> (when provided)
    /// must contain the PFX password in plain text. Both secrets are fetched from the
    /// project, environment, and secret path resolved from the <paramref name="client"/>
    /// configuration (and optional explicit overrides).
    /// </para>
    /// </summary>
    /// <param name="listenOptions">The Kestrel listen options.</param>
    /// <param name="client">An authenticated <see cref="InfisicalClient"/>.</param>
    /// <param name="pfxSecretName">The Infisical secret name holding the Base64-encoded PFX.</param>
    /// <param name="passwordSecretName">
    /// The Infisical secret name holding the PFX password. When <c>null</c>, no password is used.
    /// </param>
    /// <param name="projectId">The Infisical project ID.</param>
    /// <param name="environment">The Infisical environment slug (e.g., <c>dev</c>, <c>prod</c>).</param>
    /// <param name="secretPath">The Infisical secret path. Defaults to <c>/</c>.</param>
    /// <param name="protocols">
    /// Optional HTTP protocols to enable on this endpoint (e.g. <c>HttpProtocols.Http1AndHttp2</c>,
    /// <c>HttpProtocols.Http1AndHttp2AndHttp3</c>). When <c>null</c>, the Kestrel default is used.
    /// Applied to <see cref="ListenOptions.Protocols"/> before HTTPS is configured.
    /// </param>
    /// <param name="configureHttps">Optional callback to further configure <see cref="HttpsConnectionAdapterOptions"/>.</param>
    /// <returns>The <see cref="ListenOptions"/> for chaining.</returns>
    public static ListenOptions UseHttpsFromInfisical(
        this ListenOptions listenOptions,
        InfisicalClient client,
        string pfxSecretName,
        string? passwordSecretName,
        string projectId,
        string environment,
        string secretPath = "/",
        HttpProtocols? protocols = null,
        Action<HttpsConnectionAdapterOptions>? configureHttps = null)
    {
        ArgumentNullException.ThrowIfNull(listenOptions);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(pfxSecretName);
        ArgumentException.ThrowIfNullOrEmpty(projectId);
        ArgumentException.ThrowIfNullOrEmpty(environment);

        var certificate = LoadCertificateAsync(client, pfxSecretName, passwordSecretName, projectId, environment, secretPath)
            .GetAwaiter().GetResult();

        if (protocols.HasValue)
        {
            listenOptions.Protocols = protocols.Value;
        }

        if (configureHttps is null)
        {
            return listenOptions.UseHttps(certificate);
        }

        return listenOptions.UseHttps(options =>
        {
            options.ServerCertificate = certificate;
            configureHttps(options);
        });
    }

    /// <summary>
    /// Loads an <see cref="X509Certificate2"/> from Infisical secrets. The PFX secret value
    /// is expected to be Base64 encoded.
    /// </summary>
    public static async Task<X509Certificate2> LoadCertificateAsync(
        InfisicalClient client,
        string pfxSecretName,
        string? passwordSecretName,
        string projectId,
        string environment,
        string secretPath = "/")
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(pfxSecretName);
        ArgumentException.ThrowIfNullOrEmpty(projectId);
        ArgumentException.ThrowIfNullOrEmpty(environment);

        var pfxSecret = await client.Secrets().GetAsync(new GetSecretOptions
        {
            SecretName = pfxSecretName,
            ProjectId = projectId,
            EnvironmentSlug = environment,
            SecretPath = secretPath
        }).ConfigureAwait(false);

        if (pfxSecret is null || string.IsNullOrEmpty(pfxSecret.SecretValue))
        {
            throw new InvalidOperationException(
                $"Infisical secret '{pfxSecretName}' not found or empty in project '{projectId}', environment '{environment}', path '{secretPath}'.");
        }

        byte[] pfxBytes;
        try
        {
            pfxBytes = Convert.FromBase64String(pfxSecret.SecretValue);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Infisical secret '{pfxSecretName}' is not a valid Base64-encoded PFX value.", ex);
        }

        string? password = null;
        if (!string.IsNullOrEmpty(passwordSecretName))
        {
            var passwordSecret = await client.Secrets().GetAsync(new GetSecretOptions
            {
                SecretName = passwordSecretName,
                ProjectId = projectId,
                EnvironmentSlug = environment,
                SecretPath = secretPath
            }).ConfigureAwait(false);

            password = passwordSecret?.SecretValue;
        }

        return X509CertificateLoader.LoadPkcs12(pfxBytes, password);
    }
}
