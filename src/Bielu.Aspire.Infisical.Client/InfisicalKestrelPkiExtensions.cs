using System.Security.Cryptography.X509Certificates;
using Infisical.Sdk;
using Infisical.Sdk.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace Bielu.Aspire.Infisical.Client;

/// <summary>
/// Provides Kestrel <see cref="ListenOptions"/> extension methods that load TLS certificates
/// directly from the Infisical <b>PKI / Certificates</b> module (as opposed to the secret-based
/// <see cref="InfisicalKestrelExtensions"/> overloads).
/// <para>
/// In Infisical's PKI module a certificate is not stored under a user-defined secret name; it is
/// issued to a <b>Subscriber</b> and identified by the subscriber's name. These helpers retrieve
/// the latest certificate bundle for a subscriber and configure Kestrel to serve it.
/// </para>
/// </summary>
public static class InfisicalKestrelPkiExtensions
{
    /// <summary>
    /// Configures Kestrel to use HTTPS with the latest certificate issued for the given Infisical
    /// PKI subscriber.
    /// </summary>
    /// <param name="listenOptions">The Kestrel listen options.</param>
    /// <param name="client">An authenticated <see cref="InfisicalClient"/>.</param>
    /// <param name="subscriberName">
    /// The Infisical PKI subscriber name that identifies which certificate to retrieve.
    /// This is the "name" used by Infisical's PKI module instead of a secret key.
    /// </param>
    /// <param name="projectId">
    /// Optional Infisical project ID that owns the PKI subscriber. When <c>null</c>, the
    /// project resolved from the <paramref name="client"/> auth context is used.
    /// </param>
    /// <param name="protocols">
    /// Optional HTTP protocols to enable on this endpoint (e.g. <c>HttpProtocols.Http1AndHttp2</c>).
    /// When <c>null</c>, the Kestrel default is used.
    /// </param>
    /// <param name="configureHttps">Optional callback to further configure <see cref="HttpsConnectionAdapterOptions"/>.</param>
    /// <returns>The <see cref="ListenOptions"/> for chaining.</returns>
    public static ListenOptions UseHttpsFromInfisicalPki(
        this ListenOptions listenOptions,
        InfisicalClient client,
        string subscriberName,
        string? projectId = null,
        HttpProtocols? protocols = null,
        Action<HttpsConnectionAdapterOptions>? configureHttps = null)
    {
        ArgumentNullException.ThrowIfNull(listenOptions);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(subscriberName);

        var certificate = LoadCertificateFromPkiAsync(client, subscriberName, projectId)
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
    /// Issues a new certificate for the given Infisical PKI subscriber and configures Kestrel
    /// to use it for HTTPS. Use this overload when you want a freshly issued certificate at
    /// startup rather than the latest already-issued one.
    /// </summary>
    /// <param name="listenOptions">The Kestrel listen options.</param>
    /// <param name="client">An authenticated <see cref="InfisicalClient"/>.</param>
    /// <param name="subscriberName">The Infisical PKI subscriber name to issue a certificate for.</param>
    /// <param name="projectId">Optional Infisical project ID that owns the PKI subscriber.</param>
    /// <param name="protocols">Optional HTTP protocols to enable on this endpoint.</param>
    /// <param name="configureHttps">Optional callback to further configure <see cref="HttpsConnectionAdapterOptions"/>.</param>
    /// <returns>The <see cref="ListenOptions"/> for chaining.</returns>
    public static ListenOptions IssueHttpsFromInfisicalPki(
        this ListenOptions listenOptions,
        InfisicalClient client,
        string subscriberName,
        string? projectId = null,
        HttpProtocols? protocols = null,
        Action<HttpsConnectionAdapterOptions>? configureHttps = null)
    {
        ArgumentNullException.ThrowIfNull(listenOptions);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(subscriberName);

        var certificate = IssueCertificateFromPkiAsync(client, subscriberName, projectId)
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
    /// Retrieves the latest certificate bundle for the given Infisical PKI subscriber and
    /// returns it as an <see cref="X509Certificate2"/> with the private key attached.
    /// </summary>
    public static async Task<X509Certificate2> LoadCertificateFromPkiAsync(
        InfisicalClient client,
        string subscriberName,
        string? projectId = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(subscriberName);

        var options = new RetrieveLatestCertificateBundleOptions
        {
            SubscriberName = subscriberName,
            ProjectId = projectId!
        };

        var bundle = await client.Pki().Subscribers().RetrieveLatestCertificateBundleAsync(options)
            .ConfigureAwait(false);

        return BuildCertificate(bundle, subscriberName);
    }

    /// <summary>
    /// Issues a new certificate for the given Infisical PKI subscriber and returns it as an
    /// <see cref="X509Certificate2"/> with the private key attached.
    /// </summary>
    public static async Task<X509Certificate2> IssueCertificateFromPkiAsync(
        InfisicalClient client,
        string subscriberName,
        string? projectId = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(subscriberName);

        var options = new IssueCertificateOptions
        {
            SubscriberName = subscriberName
        };

        var issued = await client.Pki().Subscribers().IssueCertificateAsync(options)
            .ConfigureAwait(false);

        return BuildCertificate(issued, subscriberName);
    }

    private static X509Certificate2 BuildCertificate(object bundle, string subscriberName)
    {
        if (bundle is null)
        {
            throw new InvalidOperationException(
                $"Infisical PKI returned no certificate bundle for subscriber '{subscriberName}'.");
        }

        var type = bundle.GetType();
        var certPem = type.GetProperty("Certificate")?.GetValue(bundle) as string;
        var keyPem = type.GetProperty("PrivateKey")?.GetValue(bundle) as string;

        if (string.IsNullOrEmpty(certPem) || string.IsNullOrEmpty(keyPem))
        {
            throw new InvalidOperationException(
                $"Infisical PKI bundle for subscriber '{subscriberName}' is missing the certificate or private key.");
        }

        // Combine the PEM-encoded certificate and private key into a single X509Certificate2.
        using var ephemeral = X509Certificate2.CreateFromPem(certPem, keyPem);

        // Round-trip through PKCS#12 so the key is persisted in a way SChannel/Kestrel can use
        // on Windows (CreateFromPem alone produces an ephemeral key that fails TLS handshakes).
        return X509CertificateLoader.LoadPkcs12(
            ephemeral.Export(X509ContentType.Pkcs12),
            password: null);
    }
}
