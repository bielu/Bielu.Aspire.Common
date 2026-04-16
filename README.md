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

## Installation

```shell
dotnet add package Bielu.Aspire.Common
dotnet add package Bielu.Aspire.Resources
dotnet add package Bielu.Aspire.OnePassword
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

### Reverse Proxy Endpoint Hostname

Override the target hostname for an existing endpoint:

```csharp
builder.AddProject<MyProject>("my-project")
    .WithHttpEndpoint(name: "http")
    .WitHostnameForEndpoint("http", "custom-hostname");
```

## Requirements

- .NET 10.0 or later
- .NET Aspire 13.0 or later

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
