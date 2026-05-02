using Bielu.Aspire.Infisical;
using Bielu.Aspire.Resources.Containers;
#pragma warning disable ASPIRECOMPUTE003
#pragma warning disable ASPIREPIPELINES003

var builder = DistributedApplication.CreateBuilder(args);
builder.AddDockerComposeEnvironment("compose");
// var registryEndpoint = builder.AddParameterFromConfiguration("registryEndpoint", "REGISTRY_ENDPOINT");
// var registryRepository = builder.AddParameterFromConfiguration("registryRepository", "REGISTRY_REPOSITORY");
// var flexibleRegistry = builder.AddContainerRegistry(
//     "my-registry",
//     registryEndpoint,
//     registryRepository
// );
var smtp = builder.AddContainer("smtp", "rnwood/smtp4dev")
    .WithEndpoint(targetPort: 80, port: 5000, name: "http", scheme: "http")
    .WithEndpoint(targetPort: 25, port: 2525, name: "smtp")
    .WithEndpoint(targetPort: 110, port: 1110, name: "pop3")
    .WithExternalHttpEndpoints();
builder.AddDockerfile("worker-container", "../","./Bielu.Aspire.Common.Example.Worker/Dockerfile");
    var worker = builder.AddDockerfileImage("worker", "../","../Bielu.Aspire.Common.Example.Worker/Dockerfile");
var (infisical, _, _) = builder.AddInfisicalWithDependencies("infisical");
// smtp4dev is reachable from the Infisical container by its container name on the
// internal network. Use the container resource name as host and the target port (25).
infisical
    .WithSmtp(
        host: "smtp",
        port: 25,
        username: null,
        password: null,
        fromAddress: "infisical@localhost",
        fromName: "Infisical",
        // smtp4dev presents a self-signed certificate when STARTTLS is attempted.
        // Disable TLS entirely for local dev and also disable cert validation as a
        // belt-and-suspenders measure (NEVER do this in production).
        requireTls: false,
        ignoreTls: true,
        tlsRejectUnauthorized: false)
    .WaitFor(smtp);

var apiService = builder.AddProject<Projects.Bielu_Aspire_Common_Example_Blazor_ApiService>("apiservice")
    .WithHttpHealthCheck("/health").WaitForDockerfileImage(worker)
    .WithInfisicalClient(infisical);
// builder.AddContainer("worker-container-with-image",worker)
//     .WaitFor(worker);
builder.AddProject<Projects.Bielu_Aspire_Common_Example_Blazor_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
#pragma warning restore ASPIRECOMPUTE003
