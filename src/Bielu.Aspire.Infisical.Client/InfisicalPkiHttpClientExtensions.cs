using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Infisical.Sdk;
using Infisical.Sdk.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Bielu.Aspire.Infisical.Client;

/// <summary>
/// Provides extension methods that configure <see cref="HttpClient"/> instances to trust a
/// certificate (or full CA chain) issued by the Infisical <b>PKI / Certificates</b> module.
/// <para>
/// This is the PKI counterpart of <see cref="InfisicalHttpClientExtensions"/>, which loads the
/// trust anchor from a Base64-encoded PFX stored in Infisical Secrets. Use these helpers when
/// the server certificate is issued by an Infisical PKI subscriber (typically the same one
/// used by <see cref="InfisicalKestrelPkiExtensions.UseHttpsFromInfisicalPki"/>) and you want
/// outgoing <see cref="HttpClient"/> calls to trust it without disabling certificate
/// validation.
/// </para>
/// </summary>
public static class InfisicalPkiHttpClientExtensions
{
    /// <summary>
    /// Configures the primary <see cref="HttpMessageHandler"/> of the <see cref="HttpClient"/>
    /// represented by <paramref name="builder"/> to trust the certificate (and its issuing CA
    /// chain, when available) retrieved from the Infisical PKI subscriber
    /// <paramref name="subscriberName"/>, in addition to the system trust store.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/> being configured.</param>
    /// <param name="subscriberName">The Infisical PKI subscriber (profile) name.</param>
    /// <param name="projectId">
    /// Optional Infisical project ID that owns the PKI subscriber. When <c>null</c>, the
    /// project resolved from the <see cref="InfisicalClient"/> auth context is used.
    /// </param>
    /// <returns>The same <see cref="IHttpClientBuilder"/> for chaining.</returns>
    public static IHttpClientBuilder TrustInfisicalPkiCertificate(
        this IHttpClientBuilder builder,
        string subscriberName,
        string? projectId = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(subscriberName);

        return builder.ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var client = sp.GetRequiredService<InfisicalClient>();
            var trustAnchors = LoadPkiTrustAnchors(client, subscriberName, projectId);
            return CreateTrustingHandler(trustAnchors);
        });
    }

    /// <summary>
    /// Configures <b>all</b> <see cref="HttpClient"/> instances created via
    /// <c>IHttpClientFactory</c> (including typed clients) to trust the certificate / CA chain
    /// issued by the given Infisical PKI subscriber. Internally calls
    /// <see cref="HttpClientFactoryServiceCollectionExtensions.ConfigureHttpClientDefaults"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="subscriberName">The Infisical PKI subscriber (profile) name.</param>
    /// <param name="projectId">Optional Infisical project ID that owns the PKI subscriber.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection ConfigureHttpClientDefaultsToTrustInfisicalPki(
        this IServiceCollection services,
        string subscriberName,
        string? projectId = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(subscriberName);

        services.ConfigureHttpClientDefaults(http =>
            http.TrustInfisicalPkiCertificate(subscriberName, projectId));

        return services;
    }

    private static X509Certificate2[] LoadPkiTrustAnchors(
        InfisicalClient client,
        string subscriberName,
        string? projectId)
    {
        // Use the latest already-issued bundle so we get both the leaf and (when provided) the
        // issuing CA chain — those are what we want to trust for outgoing HTTPS calls.
        var leaf = InfisicalKestrelPkiExtensions
            .LoadCertificateFromPkiAsync(client, subscriberName, projectId)
            .GetAwaiter().GetResult();

        var anchors = new List<X509Certificate2> { leaf };

        // Best-effort: also pull the issuing CA / chain from the bundle if the SDK exposes it,
        // so a CA-signed cert validates without pinning the leaf.
        try
        {
            var bundleTask = client.Pki().Subscribers().RetrieveLatestCertificateBundleAsync(
                new RetrieveLatestCertificateBundleOptions
                {
                    SubscriberName = subscriberName,
                    ProjectId = projectId!
                });
            var bundle = bundleTask.GetAwaiter().GetResult();
            if (bundle is not null)
            {
                AppendPemAnchorsFromProperty(bundle, "IssuingCaCertificate", anchors);
                AppendPemAnchorsFromProperty(bundle, "CertificateChain", anchors);
            }
        }
        catch
        {
            // If the bundle can't be fetched a second time (or the SDK shape changes), fall back
            // to trusting just the leaf — that still works for direct/self-signed scenarios.
        }

        return anchors.ToArray();
    }

    private static void AppendPemAnchorsFromProperty(object bundle, string propertyName, List<X509Certificate2> anchors)
    {
        var pem = bundle.GetType().GetProperty(propertyName)?.GetValue(bundle) as string;
        if (string.IsNullOrEmpty(pem))
        {
            return;
        }

        // A "chain" PEM may contain multiple concatenated certificates.
        const string header = "-----BEGIN CERTIFICATE-----";
        const string footer = "-----END CERTIFICATE-----";

        var index = 0;
        while (true)
        {
            var start = pem.IndexOf(header, index, StringComparison.Ordinal);
            if (start < 0) break;
            var end = pem.IndexOf(footer, start, StringComparison.Ordinal);
            if (end < 0) break;
            end += footer.Length;
            var block = pem.Substring(start, end - start);
            try
            {
                anchors.Add(X509Certificate2.CreateFromPem(block));
            }
            catch
            {
                // Skip malformed entries; we only need at least one valid anchor.
            }
            index = end;
        }
    }

    private static HttpClientHandler CreateTrustingHandler(X509Certificate2[] trustAnchors)
    {
        var handler = new HttpClientHandler();

        var trustedThumbprints = new HashSet<string>(
            trustAnchors.Select(c => c.Thumbprint),
            StringComparer.OrdinalIgnoreCase);

        handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
        {
            // System trust already accepts the cert — let it through.
            if (errors == System.Net.Security.SslPolicyErrors.None)
            {
                return true;
            }

            if (cert is null)
            {
                return false;
            }

            // Direct pin on the leaf.
            if (trustedThumbprints.Contains(cert.Thumbprint))
            {
                return true;
            }

            // Build a chain with our PKI anchors as the custom root trust store.
            using var customChain = new X509Chain();
            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            foreach (var anchor in trustAnchors)
            {
                customChain.ChainPolicy.CustomTrustStore.Add(anchor);
                customChain.ChainPolicy.ExtraStore.Add(anchor);
            }

            return customChain.Build(cert);
        };

        return handler;
    }
}
