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

All `AddInfisical*` methods return an `IResourceBuilder<InfisicalResource>` which implements `IResourceWithConnectionString`, so you can use `.WithReference()` to inject the Infisical URL as a connection string:

```csharp
// AppHost (Program.cs)
var (infisical, db, redis) = builder.AddInfisicalWithDependencies("infisical");
builder.AddProject<Projects.MyApi>("myapi")
    .WithReference(infisical);
```

#### Client-side configuration (service project)

In your service project, install `Bielu.Aspire.Infisical.Client` and call `AddInfisicalConfiguration` to
wire Infisical secrets into the .NET configuration system. The Infisical server URL is resolved
automatically from the Aspire connection string:

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
