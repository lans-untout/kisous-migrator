using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kisous.Migrator.Models;

namespace Kisous.Migrator.Services;

public sealed class BootstrapMigrationFactory
{
    internal const string BootstrapMigrationId = "20260101_000000_initial_schema";
    private const string BootstrapScriptName = BootstrapMigrationId + ".sql";
    private const string BootstrapRollbackScriptName = BootstrapMigrationId + ".rollback.sql";

    private static readonly Regex DevRoleBlockPattern = new Regex(
        @"(?ms)^-- Create Silatigui_user role if not exists\s*DO\s*\$do\$.+?END\s*\$do\$;\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string BootstrapRollbackSql = @"
DO $$
DECLARE
    object_name record;
BEGIN
    FOR object_name IN
        SELECT viewname AS name
        FROM pg_views
        WHERE schemaname = 'public'
    LOOP
        EXECUTE format('DROP VIEW IF EXISTS public.%I CASCADE', object_name.name);
    END LOOP;

    FOR object_name IN
        SELECT matviewname AS name
        FROM pg_matviews
        WHERE schemaname = 'public'
    LOOP
        EXECUTE format('DROP MATERIALIZED VIEW IF EXISTS public.%I CASCADE', object_name.name);
    END LOOP;

    FOR object_name IN
        SELECT tablename AS name
        FROM pg_tables
        WHERE schemaname = 'public'
          AND tablename <> 'schema_migration_journal'
    LOOP
        EXECUTE format('DROP TABLE IF EXISTS public.%I CASCADE', object_name.name);
    END LOOP;

    FOR object_name IN
        SELECT sequencename AS name
        FROM pg_sequences
        WHERE schemaname = 'public'
    LOOP
        EXECUTE format('DROP SEQUENCE IF EXISTS public.%I CASCADE', object_name.name);
    END LOOP;
END
$$;

DROP FUNCTION IF EXISTS public.update_updated_at_column() CASCADE;
";

    public IReadOnlyList<MigrationScript> AugmentWithBootstrap(
        IReadOnlyList<MigrationScript> scripts,
        string bootstrapScriptPath)
    {
        ArgumentNullException.ThrowIfNull(scripts);

        if (scripts.Any(script => string.Equals(script.MigrationId, BootstrapMigrationId, StringComparison.Ordinal)))
        {
            return scripts;
        }

        if (string.IsNullOrWhiteSpace(bootstrapScriptPath))
        {
            throw new ArgumentException("Bootstrap script path is required.", nameof(bootstrapScriptPath));
        }

        if (!File.Exists(bootstrapScriptPath))
        {
            throw new FileNotFoundException($"Bootstrap script '{bootstrapScriptPath}' does not exist.", bootstrapScriptPath);
        }

        var bootstrapContent = PrepareBootstrapScript(File.ReadAllText(bootstrapScriptPath, Encoding.UTF8));
        var bootstrapScript = new MigrationScript(
            BootstrapMigrationId,
            bootstrapScriptPath,
            BootstrapScriptName,
            bootstrapContent,
            ComputeChecksum(bootstrapContent),
            BootstrapRollbackScriptName,
            BootstrapRollbackScriptName,
            BootstrapRollbackSql,
            ComputeChecksum(BootstrapRollbackSql));

        return new[] { bootstrapScript }
            .Concat(scripts)
            .OrderBy(script => script.MigrationId, StringComparer.Ordinal)
            .ToArray();
    }

    internal string PrepareBootstrapScript(string scriptContent)
    {
        if (string.IsNullOrWhiteSpace(scriptContent))
        {
            throw new ArgumentException("Bootstrap script content cannot be empty.", nameof(scriptContent));
        }

        var sanitized = DevRoleBlockPattern.Replace(scriptContent, string.Empty, 1);
        return sanitized.TrimStart();
    }

    private static string ComputeChecksum(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}
