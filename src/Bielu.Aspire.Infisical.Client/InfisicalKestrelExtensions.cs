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
    /// Configures Kestrel to use HTTPS with a PFX certificate fetched from Infisical, using
    /// the project, environment, and secret path resolved from <paramref name="settings"/>.
    /// <para>
    /// When <see cref="InfisicalClientSettings.SslProjectId"/>,
    /// <see cref="InfisicalClientSettings.SslEnvironment"/>, or
    /// <see cref="InfisicalClientSettings.SslSecretPath"/> are set, they are used to fetch the
    /// certificate; otherwise the corresponding application-level values
    /// (<see cref="InfisicalClientSettings.ProjectId"/>, etc.) are used. This supports topologies
    /// where TLS/SSL secrets live in a separate Infisical project from the application secrets.
    /// </para>
    /// </summary>
    /// <param name="listenOptions">The Kestrel listen options.</param>
    /// <param name="client">An authenticated <see cref="InfisicalClient"/>.</param>
    /// <param name="settings">The Infisical client settings used to resolve the SSL project/env/path.</param>
    /// <param name="pfxSecretName">The Infisical secret name holding the Base64-encoded PFX.</param>
    /// <param name="passwordSecretName">
    /// The Infisical secret name holding the PFX password. When <c>null</c>, no password is used.
    /// </param>
    /// <param name="protocols">Optional HTTP protocols to enable on this endpoint.</param>
    /// <param name="configureHttps">Optional callback to further configure <see cref="HttpsConnectionAdapterOptions"/>.</param>
    /// <returns>The <see cref="ListenOptions"/> for chaining.</returns>
    public static ListenOptions UseHttpsFromInfisical(
        this ListenOptions listenOptions,
        InfisicalClient client,
        InfisicalClientSettings settings,
        string pfxSecretName,
        string? passwordSecretName = null,
        HttpProtocols? protocols = null,
        Action<HttpsConnectionAdapterOptions>? configureHttps = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var projectId = settings.EffectiveSslProjectId;
        var environment = settings.EffectiveSslEnvironment;
        var secretPath = settings.EffectiveSslSecretPath ?? "/";

        if (string.IsNullOrEmpty(projectId))
        {
            throw new InvalidOperationException(
                "Infisical SSL project ID is not configured. Set 'Infisical:Client:SslProjectId' " +
                "(or 'Infisical:Client:ProjectId' as a fallback).");
        }

        if (string.IsNullOrEmpty(environment))
        {
            throw new InvalidOperationException(
                "Infisical SSL environment is not configured. Set 'Infisical:Client:SslEnvironment' " +
                "(or 'Infisical:Client:Environment' as a fallback).");
        }

        return listenOptions.UseHttpsFromInfisical(
            client,
            pfxSecretName,
            passwordSecretName,
            projectId,
            environment,
            secretPath,
            protocols,
            configureHttps);
    }

    /// <summary>
    /// Loads an <see cref="X509Certificate2"/> from Infisical secrets. The certificate secret
    /// value may be either a Base64-encoded PFX/PKCS#12 blob or a PEM-encoded certificate
    /// (optionally with the private key in the same PEM bundle, or a separate PEM key in
    /// <paramref name="passwordSecretName"/>'s sibling — see remarks).
    /// </summary>
    /// <remarks>
    /// When the secret value contains a PEM block (e.g. <c>-----BEGIN CERTIFICATE-----</c>),
    /// the loader treats it as PEM. If the same secret also contains a <c>-----BEGIN ... PRIVATE KEY-----</c>
    /// block (as Infisical's PKI module emits when bundling cert+key), it is combined automatically.
    /// Otherwise the value is interpreted as Base64-encoded PKCS#12 and <paramref name="passwordSecretName"/>
    /// is used as the PFX password.
    /// </remarks>
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

        var rawValue = pfxSecret.SecretValue;

        // Detect PEM (Infisical's PKI / certificate module returns PEM, not Base64 PFX).
        if (LooksLikePem(rawValue))
        {
            string? keyPem = ExtractPrivateKeyPem(rawValue);
            string certPem = ExtractCertificatePem(rawValue) ?? rawValue;

            // If the cert PEM didn't include a private key, look for one in the companion secret.
            if (keyPem is null && !string.IsNullOrEmpty(passwordSecretName))
            {
                var keySecret = await client.Secrets().GetAsync(new GetSecretOptions
                {
                    SecretName = passwordSecretName,
                    ProjectId = projectId,
                    EnvironmentSlug = environment,
                    SecretPath = secretPath
                }).ConfigureAwait(false);

                if (keySecret is not null && LooksLikePem(keySecret.SecretValue))
                {
                    keyPem = keySecret.SecretValue;
                }
            }

            if (keyPem is null)
            {
                throw new InvalidOperationException(
                    $"Infisical secret '{pfxSecretName}' contains a PEM certificate but no private key was found. " +
                    $"Either include the private-key PEM block in the same secret, or provide the key PEM via '{passwordSecretName}'.");
            }

            using var ephemeral = X509Certificate2.CreateFromPem(certPem, keyPem);

            // Round-trip through PKCS#12 so SChannel/Kestrel can use the key on Windows.
            return X509CertificateLoader.LoadPkcs12(
                ephemeral.Export(X509ContentType.Pkcs12),
                password: null);
        }

        byte[] pfxBytes;
        try
        {
            pfxBytes = Convert.FromBase64String(rawValue);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Infisical secret '{pfxSecretName}' is neither a PEM-encoded certificate nor a valid Base64-encoded PFX value.", ex);
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

    private static bool LooksLikePem(string? value)
        => !string.IsNullOrEmpty(value) && value.Contains("-----BEGIN ", StringComparison.Ordinal);

    private static string? ExtractCertificatePem(string pem)
    {
        const string begin = "-----BEGIN CERTIFICATE-----";
        const string end = "-----END CERTIFICATE-----";
        var start = pem.IndexOf(begin, StringComparison.Ordinal);
        if (start < 0) return null;
        var stop = pem.IndexOf(end, start, StringComparison.Ordinal);
        if (stop < 0) return null;
        return pem.Substring(start, stop - start + end.Length);
    }

    private static string? ExtractPrivateKeyPem(string pem)
    {
        // Matches PRIVATE KEY, RSA PRIVATE KEY, EC PRIVATE KEY, ENCRYPTED PRIVATE KEY, etc.
        var beginIdx = pem.IndexOf("-----BEGIN ", StringComparison.Ordinal);
        while (beginIdx >= 0)
        {
            var lineEnd = pem.IndexOf("-----", beginIdx + "-----BEGIN ".Length, StringComparison.Ordinal);
            if (lineEnd < 0) return null;
            var label = pem.Substring(beginIdx + "-----BEGIN ".Length, lineEnd - (beginIdx + "-----BEGIN ".Length));
            if (label.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                var endLabel = "-----END " + label + "-----";
                var endIdx = pem.IndexOf(endLabel, StringComparison.Ordinal);
                if (endIdx < 0) return null;
                return pem.Substring(beginIdx, endIdx - beginIdx + endLabel.Length);
            }
            beginIdx = pem.IndexOf("-----BEGIN ", lineEnd, StringComparison.Ordinal);
        }
        return null;
    }
}
