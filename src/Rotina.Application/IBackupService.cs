namespace Rotina.Application;

public interface IBackupService
{
    Task<string> ExportJsonAsync(CancellationToken cancellationToken = default);
    Task<string> CreateLocalBackupAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListLocalBackupsAsync(CancellationToken cancellationToken = default);
    Task RestoreLocalBackupAsync(string path, CancellationToken cancellationToken = default);
}
