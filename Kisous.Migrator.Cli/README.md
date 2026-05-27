# Kisous Migrator CLI

`Kisous.Migrator.Cli` is the command-line tool package for running Kisous database migrations from a terminal or CI pipeline.

## Install

This package is published as a .NET tool. After restoring the package source, install it with:

```bash
dotnet tool install --global Kisous.Migrator.Cli
```

If you are testing locally from this repository, you can also pack and install the tool from the generated `.nupkg`.

## Usage

Run a migration with a provider and connection string:

```bash
kisous-migrate --provider sqlserver --connection-string "Server=.;Database=AppDb;Trusted_Connection=True;" --migrations-path ./migrations
```

Supported providers:

- `sqlserver`
- `postgres`

Common options:

- `--migrations-path`: Directory containing migration scripts.
- `--target`: Target migration id to apply.
- `--connection-string`: Database connection string.

## Notes

The CLI is packaged as a tool and depends on the core migration engine plus the SQL Server, PostgreSQL, and dependency-injection projects in this repository.