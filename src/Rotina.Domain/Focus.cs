namespace Rotina.Domain;

public enum FocusStatus { Running, Paused, Finished }

public sealed record FocusSession(Guid Id, string Title, string Category, int TargetMinutes,
    DateTimeOffset StartedAt, DateTimeOffset? PausedAt, DateTimeOffset? EndedAt,
    int PausedMinutes, int? Rating, string? Note, Guid? AreaId)
{
    public FocusStatus Status => EndedAt is not null ? FocusStatus.Finished : PausedAt is not null ? FocusStatus.Paused : FocusStatus.Running;
    public int ElapsedMinutes(DateTimeOffset now) {
        var end = EndedAt ?? now;
        var minutes = (int)Math.Max(0, (end - StartedAt).TotalMinutes) - PausedMinutes;
        return Math.Max(0, minutes);
    }
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Title) || Title.Length > 120 ||
            string.IsNullOrWhiteSpace(Category) || Category.Length > 80 || TargetMinutes is < 1 or > 1440 ||
            StartedAt > DateTimeOffset.UtcNow.AddMinutes(1) || PausedMinutes < 0 ||
            Rating is < 1 or > 5 || Note?.Length > 1000)
            throw new ArgumentException("Sessão de foco inválida.");
        if (EndedAt is { } ended && ended < StartedAt) throw new ArgumentException("Fim da sessão inválido.");
    }
}
