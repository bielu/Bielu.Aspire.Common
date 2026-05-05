# Bielu.Aspire.Common

[![Build & Publish](https://github.com/bielu/Bielu.Aspire.Common/actions/workflows/buildAndPublishPackage.yml/badge.svg)](https://github.com/bielu/Bielu.Aspire.Common/actions/workflows/buildAndPublishPackage.yml)
[![NuGet](https://img.shields.io/nuget/v/Bielu.Aspire.Common.svg)](https://www.nuget.org/packages/Bielu.Aspire.Common)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A collection of common extensions and resources for .NET Aspire in the Bielu ecosystem.

## Packages

| Package | Description |
|---------|-------------|
| [Bielu.Aspire.Common](https://www.nuget.org/packages/Bielu.Aspire.Common) | Common Aspire hosting extensions (reverse proxy endpoint configuration) |
| [Bielu.Aspire.Resources](https://www.nuget.org/packages/Bielu.Aspire.Resources) | Custom Aspire resources (file store with bind mount and volume support) |
| [Bielu.Aspire.OnePassword](https://www.nuget.org/packages/Bielu.Aspire.OnePassword) | 1Password Connect integration for Aspire |
| [Bielu.Aspire.Infisical](https://www.nuget.org/packages/Bielu.Aspire.Infisical) | Infisical secrets management integration for Aspire |
| [Bielu.Aspire.Infisical.Client](https://www.nuget.org/packages/Bielu.Aspire.Infisical.Client) | Infisical configuration provider client for Aspire service projects |

## Installation

```shell
dotnet add package Bielu.Aspire.Common
dotnet add package Bielu.Aspire.Resources
dotnet add package Bielu.Aspire.OnePassword
dotnet add package Bielu.Aspire.Infisical
dotnet add package Bielu.Aspire.Infisical.Client
```

## Usage

### File Store Resource

Register a file store as a bind mount or volume for container resources:

```csharp
var store = builder.AddFileStore("my-store", "relative/path");
builder.AddContainer("my-container", "my-image")
    .WithStorage(store, "/container/path");
```

### 1Password Connect

Add 1Password Connect API and Sync containers to your Aspire app host:

```csharp
var (syncApi, connectApi) = builder.AddOnePasswordConnect();
```

### Infisical Secrets Management

Add a self-hosted [Infisical](https://infisical.com/) secrets management container with dedicated PostgreSQL and Redis/Valkey dependencies.

#### With Redis (default cache)

```csharp
var (infisical, db, redis) = builder.AddInfisicalWithDependencies("infisical");
```

#### With Valkey cache

```csharp
var (infisical, db, valkey) = builder.AddInfisicalWithValkeyDependencies("infisical");
```

#### Using existing resources

Share PostgreSQL and cache instances across services:

```csharp
var redis = builder.AddRedis("redis");
var postgres = builder.AddPostgres("postgres").AddDatabase("mydb");
var infisical = builder.AddInfisicalUsingResources(postgres, redis);
```

#### Referencing Infisical from a service project

All `AddInfisical*` methods return an `IResourceBuilder<InfisicalResource>` which implements `IResourceWithConnectionString` and `IResourceWithWaitSupport`, so you can use `.WithReference()` and `.WaitFor()`:

```csharp
// AppHost (Program.cs)
var (infisical, db, redis) = builder.AddInfisicalWithDependencies("infisical");
builder.AddProject<Projects.MyApi>("myapi")
    .WithReference(infisical)
    .WaitFor(infisical);
```

#### Automatic client configuration from AppHost (recommended)

Client credentials (ClientId, ClientSecret, ProjectId, etc.) are automatically read from the
`Infisical:Client` section in the AppHost's configuration and stored on the `InfisicalResource`.
When you call `WithInfisicalClient`, these values are injected as environment variables into
the consuming project — **no manual configuration needed in each service project**.

```json
// AppHost appsettings.json (or user-secrets)
{
  "Infisical": {
    "EncryptionKey": "...",
    "AuthSecret": "...",
    "Client": {
      "ProjectId": "<your-project-id>",
      "Environment": "dev",
      "ClientId": "<machine-identity-client-id>",
      "ClientSecret": "<machine-identity-client-secret>"
    }
  }
}
```

```csharp
// AppHost (Program.cs)
var (infisical, db, redis) = builder.AddInfisicalWithDependencies("infisical");

// Client config flows automatically from Infisical:Client section
builder.AddProject<Projects.MyApi>("myapi")
    .WithInfisicalClient(infisical);
```

```csharp
// MyApi Service (Program.cs) — no settings needed!
builder.AddInfisicalConfiguration("infisical");

// Secrets from Infisical are now available via IConfiguration
var secret = builder.Configuration["MY_SECRET"];
```

You can also configure or override client settings programmatically at the resource level
with `WithClientConfiguration`, or per-service via the optional callback on `WithInfisicalClient`:

```csharp
// Override at resource level (applies to all services)
var (infisical, db, redis) = builder.AddInfisicalWithDependencies("infisical");
infisical.WithClientConfiguration(client =>
{
    client.ProjectId = "<your-project-id>";
    client.Environment = "dev";
    client.ClientId = "<machine-identity-client-id>";
    client.ClientSecret = "<machine-identity-client-secret>";
});

// Override per-service (e.g., different environment for a specific service)
builder.AddProject<Projects.MyApi>("myapi")
    .WithInfisicalClient(infisical, client =>
    {
        client.Environment = "staging";
    });
```

`WithInfisicalClient` injects environment variables (`Infisical__Client__ProjectId`, etc.) that
the .NET configuration system maps to `Infisical:Client:*`, which `AddInfisicalConfiguration`
reads automatically. It also calls `.WithReference(infisical)` and `.WaitFor(infisical)` under
the hood.

#### Client-side configuration (service project)

In your service project, install `Bielu.Aspire.Infisical.Client` and call `AddInfisicalConfiguration` to
wire Infisical secrets into the .NET configuration system. The Infisical server URL is resolved
automatically from the Aspire connection string. Powered by
[JJConsulting.Infisical](https://github.com/JJConsulting/Infisical).

##### Machine Identity auth

```csharp
// MyApi Service (Program.cs)
builder.AddInfisicalConfiguration("infisical", settings =>
{
    settings.ProjectId = "<your-project-id>";
    settings.Environment = "dev";
    settings.ClientId = "<machine-identity-client-id>";
    settings.ClientSecret = "<machine-identity-client-secret>";
});

// Secrets from Infisical are now available via IConfiguration
var secret = builder.Configuration["MY_SECRET"];
```

##### Service Token auth

```csharp
builder.AddInfisicalConfiguration("infisical", settings =>
{
    settings.ProjectId = "<your-project-id>";
    settings.Environment = "dev";
    settings.ServiceToken = "<your-service-token>";
});
```

Client settings can also be bound from the `Infisical:Client` configuration section instead of
(or in addition to) the callback:

```json
{
  "Infisical": {
    "Client": {
      "ProjectId": "<your-project-id>",
      "Environment": "dev",
      "ClientId": "<machine-identity-client-id>",
      "ClientSecret": "<machine-identity-client-secret>"
    }
  }
}
```

In addition to the configuration provider, `AddInfisicalConfiguration` also registers
`IInfisicalSecretsService` and `IInfisicalAuthenticationService` in DI for direct secret access.

#### Standalone (all config via `Infisical:*` section)

```csharp
var infisical = builder.AddInfisical("infisical");
```

#### Configuration

The following configuration keys are read from the `Infisical` section (e.g. `appsettings.json` or user secrets):

| Key | Required | Default |
|-----|----------|---------|
| `EncryptionKey` | ✓ | — |
| `AuthSecret` | ✓ | — |
| `DbConnectionUri` | ✓ (standalone only) | — |
| `RedisUrl` | ✓ (standalone only) | — |
| `SiteUrl` | | `http://localhost:8080` |
| `TelemetryEnabled` | | `false` |

### Kestrel HTTPS from Infisical PKI

Infisical's **PKI / Certificates** module doesn't store certificates under a user-defined name —
each certificate is issued for a **Subscriber**, identified by `SubscriberName`. Use these
extensions when you want Kestrel to consume a PKI-issued certificate directly (no PFX upload):

```csharp
builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var infisical = kestrel.ApplicationServices.GetRequiredService<InfisicalClient>();

    kestrel.ListenAnyIP(443, listen =>
    {
        // Use the latest already-issued certificate for the subscriber:
        listen.UseHttpsFromInfisicalPki(
            client:         infisical,
            subscriberName: "my-api.example.com",
            protocols:      HttpProtocols.Http1AndHttp2);

        // Or issue a fresh certificate at startup:
        // listen.IssueHttpsFromInfisicalPki(infisical, "my-api.example.com");
    });
});
```

If your subscriber lives in a different Infisical project than the one resolved from the client's
auth context, pass it explicitly via the `projectId` parameter.

#### One-line bootstrap

If you don't want to wire up `ConfigureKestrel`/`ListenAnyIP` yourself, use the
`UseInfisicalPkiHttps` helper directly on `WebApplicationBuilder`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddInfisicalConfiguration("infisical");

// Binds Kestrel to :443 with HTTPS using the latest cert from the PKI subscriber.
builder.UseInfisicalPkiHttps("my-api-prod");

// Or issue a fresh certificate at startup, on a custom port:
// builder.UseInfisicalPkiHttps("my-api-prod", port: 8443, issueNew: true);

var app = builder.Build();
app.Run();
```

The `InfisicalClient` is resolved from DI, so `AddInfisicalConfiguration` (or any other
registration of `InfisicalClient`) must be called first.

> Use `UseHttpsFromInfisical` (secret-based) when the certificate is stored as a Base64-encoded
> PFX in Infisical Secrets, and `UseHttpsFromInfisicalPki` when it is managed by Infisical's PKI
> module.

#### Trusting the PKI certificate from `HttpClient`

When *calling* a service that presents an Infisical PKI–issued certificate, you can teach
every `HttpClient` (including typed clients) to trust it without disabling validation, via
`ConfigureHttpClientDefaults`:

```csharp
// Applies to ALL HttpClients created through IHttpClientFactory:
builder.Services.ConfigureHttpClientDefaultsToTrustInfisicalPki("my-api-prod");

// Or per-client:
builder.Services.AddHttpClient<MyApiClient>()
    .TrustInfisicalPkiCertificate("my-api-prod");
```

The handler trusts the leaf cert by thumbprint and, when available, builds a custom chain
using the subscriber's issuing CA / chain PEM as the root, so CA-signed certs validate
without pinning. The system trust store is still honored — only failing validations fall
back to the Infisical anchors.

### Reverse Proxy Endpoint Hostname

Override the target hostname for an existing endpoint:

```csharp
builder.AddProject<MyProject>("my-project")
    .WithHttpEndpoint(name: "http")
    .WitHostnameForEndpoint("http", "custom-hostname");
```

## Requirements

- .NET 10.0 or later
- .NET Aspire 13.2 or later

## Building

```shell
dotnet build src/Bielu.Aspire.Common.sln
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

## Author

Arkadiusz Biel ([@bielu](https://github.com/bielu))

❤️ This project is royalty free and open source, so if you are using and love it, you can support it by becoming a [GitHub sponsor](https://github.com/sponsors/bielu) ❤️
