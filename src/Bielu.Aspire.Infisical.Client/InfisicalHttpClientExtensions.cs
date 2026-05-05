using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Infisical.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Bielu.Aspire.Infisical.Client;

/// <summary>
/// Provides extension methods that configure <see cref="HttpClient"/> instances created via
/// <c>IServiceCollection.AddHttpClient</c> to trust a TLS certificate fetched from Infisical.
/// <para>
/// This is useful when calling internal services that present a self-signed (or
/// privately-issued) certificate whose root/leaf is stored as a PFX in Infisical: instead of
/// disabling certificate validation, the imported certificate is added to a per-client trust
/// list and accepted in the server certificate validation callback.
/// </para>
/// </summary>
public static class InfisicalHttpClientExtensions
{
    /// <summary>
    /// Configures the primary <see cref="HttpMessageHandler"/> of the <see cref="HttpClient"/>
    /// represented by <paramref name="builder"/> to trust the certificate stored in Infisical
    /// under <paramref name="pfxSecretName"/>, in addition to the system trust store.
    /// <para>
    /// The certificate is loaded once (lazily on first request) using the
    /// <see cref="InfisicalClient"/> resolved from the service provider and the
    /// <see cref="InfisicalClientSettings"/> (using the <c>EffectiveSsl*</c> properties to
    /// resolve project, environment, and secret path).
    /// </para>
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/> being configured.</param>
    /// <param name="pfxSecretName">The Infisical secret name holding the Base64-encoded PFX.</param>
    /// <param name="passwordSecretName">
    /// Optional Infisical secret name holding the PFX password. When <c>null</c>, no password is used.
    /// </param>
    /// <returns>The same <see cref="IHttpClientBuilder"/> for chaining.</returns>
    public static IHttpClientBuilder TrustInfisicalCertificate(
        this IHttpClientBuilder builder,
        string pfxSecretName,
        string? passwordSecretName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(pfxSecretName);

        return builder.ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var client = sp.GetRequiredService<InfisicalClient>();
            var settings = sp.GetRequiredService<InfisicalClientSettings>();
            var certificate = LoadTrustedCertificate(client, settings, pfxSecretName, passwordSecretName);
            return CreateTrustingHandler(certificate);
        });
    }

    /// <summary>
    /// Configures the default <see cref="HttpClient"/> (the one created via
    /// <c>IHttpClientFactory.CreateClient()</c> with no name) to trust the certificate stored
    /// in Infisical under <paramref name="pfxSecretName"/>, in addition to the system trust store.
    /// <para>
    /// Internally this calls <c>services.AddHttpClient(Options.DefaultName)</c> and applies
    /// <see cref="TrustInfisicalCertificate(IHttpClientBuilder, string, string?)"/>.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="pfxSecretName">The Infisical secret name holding the Base64-encoded PFX.</param>
    /// <param name="passwordSecretName">
    /// Optional Infisical secret name holding the PFX password. When <c>null</c>, no password is used.
    /// </param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for the default client, for chaining.</returns>
    public static IHttpClientBuilder AddInfisicalTrustedHttpClient(
        this IServiceCollection services,
        string pfxSecretName,
        string? passwordSecretName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Ensure the settings are available for the handler factory.
        services.AddInfisicalClientSettings();

        return services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .TrustInfisicalCertificate(pfxSecretName, passwordSecretName);
    }

    /// <summary>
    /// Registers <see cref="InfisicalClientSettings"/> as a singleton bound from the
    /// <c>Infisical:Client</c> configuration section, if it has not already been registered.
    /// This is required so that <see cref="TrustInfisicalCertificate"/> can resolve the
    /// effective SSL project/environment/path from configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddInfisicalClientSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var section = configuration.GetSection("Infisical:Client");
            return new InfisicalClientSettings
            {
                ProjectId = section["ProjectId"],
                Environment = section["Environment"],
                SecretPath = section["SecretPath"],
                ServiceToken = section["ServiceToken"],
                ClientId = section["ClientId"],
                ClientSecret = section["ClientSecret"],
                SslProjectId = section["SslProjectId"],
                SslEnvironment = section["SslEnvironment"],
                SslSecretPath = section["SslSecretPath"]
            };
        });

        return services;
    }

    private static X509Certificate2 LoadTrustedCertificate(
        InfisicalClient client,
        InfisicalClientSettings settings,
        string pfxSecretName,
        string? passwordSecretName)
    {
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

        return InfisicalKestrelExtensions.LoadCertificateAsync(
                client, pfxSecretName, passwordSecretName, projectId, environment, secretPath)
            .GetAwaiter().GetResult();
    }

    private static HttpClientHandler CreateTrustingHandler(X509Certificate2 trusted)
    {
        var handler = new HttpClientHandler();

        // Compute the trusted thumbprint once.
        var trustedThumbprint = trusted.Thumbprint;

        handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
        {
            // Default-success: if the system trust store already accepts the cert, allow it.
            if (errors == System.Net.Security.SslPolicyErrors.None)
            {
                return true;
            }

            if (cert is null)
            {
                return false;
            }

            // Direct match on the leaf certificate.
            if (string.Equals(cert.Thumbprint, trustedThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Otherwise, attempt to build a chain that includes the imported certificate as a
            // custom trust anchor. This supports the case where the Infisical-stored PFX is a
            // CA (or intermediate) that signs the server certificate.
            using var customChain = new X509Chain();
            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            customChain.ChainPolicy.CustomTrustStore.Add(trusted);
            customChain.ChainPolicy.ExtraStore.Add(trusted);

            return customChain.Build(cert);
        };

        return handler;
    }
}
