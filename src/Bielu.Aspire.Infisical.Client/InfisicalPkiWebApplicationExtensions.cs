using Infisical.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;

namespace Bielu.Aspire.Infisical.Client;

/// <summary>
/// One-line bootstrap helpers that configure Kestrel to serve HTTPS using a certificate
/// fetched from Infisical's PKI / Certificates module, without having to call
/// <see cref="WebHostBuilderKestrelExtensions.ConfigureKestrel(IWebHostBuilder, Action{KestrelServerOptions})"/>
/// or <c>ListenAnyIP</c> manually.
/// </summary>
public static class InfisicalPkiWebApplicationExtensions
{
    private const int DefaultHttpsPort = 443;

    /// <summary>
    /// Configures Kestrel on the given <see cref="WebApplicationBuilder"/> to listen on
    /// <paramref name="port"/> with HTTPS using a certificate retrieved from Infisical PKI for
    /// the specified subscriber. The <see cref="InfisicalClient"/> is resolved from DI, so make
    /// sure it has been registered (e.g. via <c>AddInfisicalConfiguration</c>) before the app runs.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="subscriberName">The Infisical PKI subscriber (profile) name.</param>
    /// <param name="port">The TCP port to bind. Defaults to <c>443</c>.</param>
    /// <param name="issueNew">
    /// When <c>true</c>, issues a fresh certificate at startup; when <c>false</c> (default),
    /// uses the latest already-issued certificate bundle for the subscriber.
    /// </param>
    /// <param name="projectId">
    /// Optional Infisical project ID that owns the PKI subscriber. When <c>null</c>, the
    /// project resolved from the <see cref="InfisicalClient"/> auth context is used.
    /// </param>
    /// <param name="protocols">
    /// Optional HTTP protocols to enable on the endpoint. Defaults to
    /// <see cref="HttpProtocols.Http1AndHttp2"/>.
    /// </param>
    /// <param name="configureHttps">Optional callback to further configure <see cref="HttpsConnectionAdapterOptions"/>.</param>
    /// <returns>The same <see cref="WebApplicationBuilder"/> for chaining.</returns>
    public static WebApplicationBuilder UseInfisicalPkiHttps(
        this WebApplicationBuilder builder,
        string subscriberName,
        int port = DefaultHttpsPort,
        bool issueNew = false,
        string? projectId = null,
        HttpProtocols? protocols = HttpProtocols.Http1AndHttp2,
        Action<HttpsConnectionAdapterOptions>? configureHttps = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(subscriberName);

        builder.WebHost.UseInfisicalPkiHttps(
            subscriberName,
            port,
            issueNew,
            projectId,
            protocols,
            configureHttps);

        return builder;
    }

    /// <summary>
    /// <see cref="IWebHostBuilder"/> variant of
    /// <see cref="UseInfisicalPkiHttps(WebApplicationBuilder, string, int, bool, string?, HttpProtocols?, Action{HttpsConnectionAdapterOptions}?)"/>.
    /// </summary>
    public static IWebHostBuilder UseInfisicalPkiHttps(
        this IWebHostBuilder webHost,
        string subscriberName,
        int port = DefaultHttpsPort,
        bool issueNew = false,
        string? projectId = null,
        HttpProtocols? protocols = HttpProtocols.Http1AndHttp2,
        Action<HttpsConnectionAdapterOptions>? configureHttps = null)
    {
        ArgumentNullException.ThrowIfNull(webHost);
        ArgumentException.ThrowIfNullOrEmpty(subscriberName);

        return webHost.ConfigureKestrel((context, kestrel) =>
        {
            var client = kestrel.ApplicationServices.GetService<InfisicalClient>()
                         ?? throw new InvalidOperationException(
                             $"No {nameof(InfisicalClient)} is registered in DI. " +
                             "Call AddInfisicalConfiguration(...) (or otherwise register InfisicalClient) " +
                             "before using UseInfisicalPkiHttps.");

            kestrel.ListenAnyIP(port, listen =>
            {
                if (issueNew)
                {
                    listen.IssueHttpsFromInfisicalPki(client, subscriberName, projectId, protocols, configureHttps);
                }
                else
                {
                    listen.UseHttpsFromInfisicalPki(client, subscriberName, projectId, protocols, configureHttps);
                }
            });
        });
    }
}
