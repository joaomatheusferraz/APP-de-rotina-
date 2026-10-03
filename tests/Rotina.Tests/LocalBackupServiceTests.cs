using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Tests;

public sealed class LocalBackupServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rotina-backup-tests", Guid.NewGuid().ToString());
    private string DatabasePath => Path.Combine(directory, "rotina.db");

    [Fact]
    public async Task BackupCanRestoreSavedProfile()
    {
        var store = new SqliteLocalStore(DatabasePath);
        var backup = new LocalBackupService(store, DatabasePath);
        await store.SaveProfileAsync(new UserProfile("Antes", ThemePreference.Dark));

        var backupPath = await backup.CreateLocalBackupAsync();
        await store.SaveProfileAsync(new UserProfile("Depois", ThemePreference.Light));
        await backup.RestoreLocalBackupAsync(backupPath);

        Assert.Equal("Antes", (await store.GetProfileAsync()).DisplayName);
    }

    [Fact]
    public async Task RestoreRejectsFileInDirectoryWithSamePrefix()
    {
        var store = new SqliteLocalStore(DatabasePath);
        var backup = new LocalBackupService(store, DatabasePath);
        await store.InitializeAsync();

        var siblingDirectory = Path.Combine(directory, "backups-other");
        Directory.CreateDirectory(siblingDirectory);
        var outsidePath = Path.Combine(siblingDirectory, "outside.db");
        File.Copy(DatabasePath, outsidePath);

        await Assert.ThrowsAsync<ArgumentException>(() => backup.RestoreLocalBackupAsync(outsidePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
