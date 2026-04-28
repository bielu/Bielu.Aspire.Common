using Bielu.Aspire.Resources.Containers;

var builder = DistributedApplication.CreateBuilder(args);
// Option A: Hardcoded values
// Option B: Using environment variables for CI/CD
var registryEndpoint = builder.AddParameterFromConfiguration("registryEndpoint", "REGISTRY_ENDPOINT");
var registryRepository = builder.AddParameterFromConfiguration("registryRepository", "REGISTRY_REPOSITORY");
#pragma warning disable ASPIRECOMPUTE003
var flexibleRegistry = builder.AddContainerRegistry(
    "my-registry",
    registryEndpoint,
    registryRepository
);
#pragma warning restore ASPIRECOMPUTE003
builder.AddDockerfile("worker-container", "../","./Bielu.Aspire.Common.Example.Worker/Dockerfile");
#pragma warning disable ASPIREPIPELINES003
#pragma warning disable ASPIRECOMPUTE003
var worker = builder.AddDockerfileImage("worker", "../","../Bielu.Aspire.Common.Example.Worker/Dockerfile").WithContainerRegistry(flexibleRegistry);
#pragma warning restore ASPIRECOMPUTE003
#pragma warning restore ASPIREPIPELINES003
var apiService = builder.AddProject<Projects.Bielu_Aspire_Common_Example_Blazor_ApiService>("apiservice")
    .WithHttpHealthCheck("/health").WaitForDockerfileImage(worker);

builder.AddProject<Projects.Bielu_Aspire_Common_Example_Blazor_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
