using Aspire.Hosting.ApplicationModel;

namespace Bielu.Aspire.Infisical;

/// <summary>
/// A resource that represents a self-hosted Infisical secrets management server.
/// </summary>
/// <param name="name">The name of the resource.</param>
public class InfisicalResource(string name) : ContainerResource(name), IResourceWithConnectionString
{
    internal const string HttpEndpointName = "http";

    private EndpointReference? _httpEndpoint;

    /// <summary>
    /// Gets the HTTP endpoint for the Infisical server.
    /// </summary>
    public EndpointReference HttpEndpoint =>
        _httpEndpoint ??= new(this, HttpEndpointName);

    /// <summary>
    /// Gets the connection string expression for the Infisical server.
    /// Returns the full HTTP URL (e.g., <c>http://host:port</c>).
    /// </summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"http://{HttpEndpoint.Property(EndpointProperty.Host)}:{HttpEndpoint.Property(EndpointProperty.Port)}");
}
