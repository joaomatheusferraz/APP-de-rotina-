using System.Text.Json;
using Microsoft.Data.Sqlite;
using Rotina.Application;

namespace Rotina.Infrastructure;

public sealed class LocalBackupService(SqliteLocalStore store, string databasePath) : IBackupService
{
    private readonly string databaseFullPath = Path.GetFullPath(databasePath);
    private readonly string backupDirectory = Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "backups");

    public async Task<string> ExportJsonAsync(CancellationToken cancellationToken = default)
    {
        await store.InitializeAsync(cancellationToken);
        Directory.CreateDirectory(backupDirectory);

        await using var connection = await OpenDatabaseAsync(cancellationToken);
        var export = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);

        foreach (var tableName in await GetTableNamesAsync(connection, cancellationToken))
            export[tableName] = await ReadTableAsync(connection, tableName, cancellationToken);

        var path = Path.Combine(backupDirectory, $"rotina-export-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    public async Task<string> CreateLocalBackupAsync(CancellationToken cancellationToken = default)
    {
        await store.InitializeAsync(cancellationToken);
        Directory.CreateDirectory(backupDirectory);

        var path = Path.Combine(backupDirectory, $"rotina-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        File.Copy(databaseFullPath, path, overwrite: true);
        return path;
    }

    public Task<IReadOnlyList<string>> ListLocalBackupsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> backups = Directory.Exists(backupDirectory)
            ? Directory.GetFiles(backupDirectory, "*.db").OrderByDescending(path => path).ToList()
            : [];
        return Task.FromResult(backups);
    }

    public async Task RestoreLocalBackupAsync(string path, CancellationToken cancellationToken = default)
    {
        var backupPath = ValidateBackupPath(path);
        await store.InitializeAsync(cancellationToken);
        File.Copy(backupPath, databaseFullPath, overwrite: true);
    }

    private async Task<SqliteConnection> OpenDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFullPath,
            Pooling = false
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<List<string>> GetTableNamesAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var tableNames = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
            tableNames.Add(reader.GetString(0));
        return tableNames;
    }

    private static async Task<List<Dictionary<string, object?>>> ReadTableAsync(
        SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM [{tableName.Replace("]", "]]", StringComparison.Ordinal)}];";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>();
            for (var column = 0; column < reader.FieldCount; column++)
                row[reader.GetName(column)] = reader.IsDBNull(column) ? null : reader.GetValue(column);
            rows.Add(row);
        }
        return rows;
    }

    private string ValidateBackupPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Backup local inválido.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var backupRoot = Path.GetFullPath(backupDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
            throw new ArgumentException("Backup local inválido.", nameof(path));

        return fullPath;
    }
}
