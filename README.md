# Kisous Migrator

[![CI](https://github.com/lans-untout/kisous-migrator/actions/workflows/ci.yml/badge.svg)](https://github.com/lans-untout/kisous-migrator/actions/workflows/ci.yml)

A robust, extensible, and high-performance database migration tool and framework for .NET applications.

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Getting Started](#getting-started)
- [Usage](#usage)
  - [CLI](#cli)
  - [Dependency Injection Pipeline](#dependency-injection-pipeline)
- [Supported Databases](#supported-databases)
- [Contributing](#contributing)
- [License](#license)

## Features

- **Code-First & Script-Based Migrations:** Supports execution of classic SQL scripts alongside code-based migrations.
- **Pluggable Architecture:** Easily integrate with existing patterns or build your own custom database provider.
- **Dependency Injection Support:** Ships with first-class Microsoft.Extensions.DependencyInjection integration.
- **CI/CD Ready CLI:** Contains a dedicated CLI tool to easily slot into DevOps pipelines.

## Architecture

The project is structured into modular components:

- `Kisous.Migrator`: Core abstractions and engine models (`MigrationPlan`, `MigrationJournalEntry`, etc.)
- `Kisous.Migrator.Cli`: Command-Line Interface.
- `Kisous.Migrator.Extensions.DependencyInjection`: Setup and DI extensions for ASP.NET / Worker services.
- Database Providers:
  - `Kisous.Migrator.SqlServer`
  - `Kisous.Migrator.PostgreSql`

## Getting Started

*(Packages are published to GitHub Packages from the release workflow.)*

In your .NET 8 or .NET 10 project, install the core package and the provider you need:

```bash
dotnet add package Kisous.Migrator
dotnet add package Kisous.Migrator.Extensions.DependencyInjection

# Choose your provider:
dotnet add package Kisous.Migrator.SqlServer
# or
dotnet add package Kisous.Migrator.PostgreSql
```

## Reuse The Library

You can consume this library in two ways:

### As A Package

If you are using the published packages from GitHub Packages or another NuGet feed, add the packages you need to your project:

```bash
dotnet add package Kisous.Migrator
dotnet add package Kisous.Migrator.Extensions.DependencyInjection
dotnet add package Kisous.Migrator.SqlServer
# or
dotnet add package Kisous.Migrator.PostgreSql
```

If you use GitHub Packages, make sure your NuGet source is configured and authenticated for the repository owner that publishes the packages.

```xml
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/<OWNER>/index.json" />
  </packageSources>
</configuration>
```

Replace `<OWNER>` with the GitHub organization or user that publishes the package.

### As A Local Project Reference

If you are working inside this repository, or you want to reuse the code before the packages are published, reference the projects directly:

```bash
dotnet add reference <path-to-repo>/Kisous.Migrator/Kisous.Migrator.csproj
dotnet add reference <path-to-repo>/Kisous.Migrator.Extensions.DependencyInjection/Kisous.Migrator.Extensions.DependencyInjection.csproj
dotnet add reference <path-to-repo>/Kisous.Migrator.SqlServer/Kisous.Migrator.SqlServer.csproj
```

Once referenced, register the migrator in your app:

```csharp
using Kisous.Migrator.Extensions.DependencyInjection;

builder.Services.AddKisousMigrator(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    // options.UsePostgreSql(...);
});
```

## Usage

### Dependency Injection Pipeline

Add the migrator to your service collection in `Program.cs` or `Startup.cs`:

```csharp
using Kisous.Migrator.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Configure Migrator with SQL Server Provider
builder.Services.AddKisousMigrator(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    // options.UsePostgreSql(...);
});
```

### CLI 

You can use the CLI directly in your terminal or CI environment:

```bash
# Example syntax
kisous-migrator run --provider SqlServer --connection-string "..." 
```

## Supported Databases

- **SQL Server**
- **PostgreSQL**
- *(More providers coming soon!)*

## Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add an amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request
