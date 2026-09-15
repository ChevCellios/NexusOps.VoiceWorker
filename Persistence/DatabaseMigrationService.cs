using System.Security.Cryptography;
using Npgsql;

namespace NexusOps.VoiceWorker.Persistence;

public sealed class DatabaseMigrationService(
    NpgsqlDataSource dataSource,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DatabaseMigrationService> logger) : IHostedService
{
    private const long AdvisoryLockId = 0x564F4943454D4947;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("DatabaseMigrations:Enabled", true))
        {
            logger.LogInformation("Automatic database migrations are disabled.");
            return;
        }

        var directory = Path.Combine(environment.ContentRootPath, "Database", "Migrations");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Database migration directory was not found: {directory}");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var lockCommand = new NpgsqlCommand("select pg_advisory_lock($1)", connection);
        lockCommand.Parameters.AddWithValue(AdvisoryLockId);
        await lockCommand.ExecuteNonQueryAsync(cancellationToken);

        try
        {
            await EnsureHistoryTableAsync(connection, cancellationToken);
            foreach (var path in Directory.EnumerateFiles(directory, "*.sql").Order(StringComparer.Ordinal))
                await ApplyMigrationAsync(connection, path, cancellationToken);
        }
        finally
        {
            await using var unlockCommand = new NpgsqlCommand("select pg_advisory_unlock($1)", connection);
            unlockCommand.Parameters.AddWithValue(AdvisoryLockId);
            await unlockCommand.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureHistoryTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            create table if not exists nexusops_schema_migrations (
                migration_id varchar(255) primary key,
                checksum char(64) not null,
                applied_at timestamptz not null default now()
            )
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ApplyMigrationAsync(NpgsqlConnection connection, string path, CancellationToken cancellationToken)
    {
        var migrationId = Path.GetFileName(path);
        var sql = await File.ReadAllTextAsync(path, cancellationToken);
        var checksum = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();

        await using var check = new NpgsqlCommand("select checksum from nexusops_schema_migrations where migration_id=$1", connection);
        check.Parameters.AddWithValue(migrationId);
        var existingChecksum = (string?)await check.ExecuteScalarAsync(cancellationToken);
        if (existingChecksum is not null)
        {
            if (!string.Equals(existingChecksum, checksum, StringComparison.Ordinal))
                throw new InvalidOperationException($"Applied database migration '{migrationId}' has been modified.");
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var migration = new NpgsqlCommand(sql, connection, transaction);
        await migration.ExecuteNonQueryAsync(cancellationToken);
        await using var record = new NpgsqlCommand(
            "insert into nexusops_schema_migrations (migration_id, checksum) values ($1,$2)", connection, transaction);
        record.Parameters.AddWithValue(migrationId);
        record.Parameters.AddWithValue(checksum);
        await record.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Applied database migration {MigrationId}.", migrationId);
    }
}
