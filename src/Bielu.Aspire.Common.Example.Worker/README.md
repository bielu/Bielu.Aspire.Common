# Example Worker

A sample .NET Worker Service that demonstrates how to create containerized applications managed by the Microservices Orchestrator.

## Features

- Simple background worker that logs messages every 5 seconds
- Configurable via environment variables:
  - `WORKER_ID`: Unique identifier for the worker instance
  - `ENVIRONMENT`: Environment name (dev, staging, production)
- Graceful startup and shutdown with logging
- Containerized with multi-stage Dockerfile for optimized image size

## Building the Docker Image

```bash
cd src/Bielu.Microservices.Orchestrator.Examples.Worker
docker build -t example-worker:latest .
```

## Running Locally with Docker

```bash
docker run -e WORKER_ID=test-1 -e ENVIRONMENT=dev example-worker:latest
```

## Using with the Orchestrator API

Create a single worker:
```bash
POST /api/containers
{
  "image": "example-worker:latest",
  "name": "orchestrator-worker",
  "environmentVariables": {
    "WORKER_ID": "worker-001",
    "ENVIRONMENT": "production"
  }
}
```

Create multiple replicas:
```bash
POST /api/containers
{
  "image": "example-worker:latest",
  "name": "worker-cluster",
  "replicas": 3,
  "environmentVariables": {
    "ENVIRONMENT": "production"
  }
}
```

## Aspire Integration

When running via the AppHost, the worker image is automatically built and made available as `example-worker:latest`.
