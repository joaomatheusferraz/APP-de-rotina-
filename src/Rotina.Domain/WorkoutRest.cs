namespace Rotina.Domain;

public sealed class WorkoutRest
{
    public int DurationSeconds { get; set; } = 90;
    public double RemainingSeconds { get; set; } = 90;
    public DateTimeOffset? Deadline { get; set; }
    public bool Started { get; set; }
    public double Remaining(DateTimeOffset now) => Deadline is { } end
        ? Math.Max(0, (end - now).TotalSeconds) : RemainingSeconds;
    public void Start(DateTimeOffset now)
    {
        RemainingSeconds = DurationSeconds;
        Deadline = now.AddSeconds(DurationSeconds);
        Started = true;
    }
    public void Pause(DateTimeOffset now)
    {
        RemainingSeconds = Remaining(now);
        Deadline = null;
    }
    public void Resume(DateTimeOffset now)
    {
        if (RemainingSeconds > 0) Deadline = now.AddSeconds(RemainingSeconds);
    }
    public void Extend(DateTimeOffset now)
    {
        if (!Started) Start(now);
        RemainingSeconds = Remaining(now) + 15;
        if (Deadline is not null) Deadline = now.AddSeconds(RemainingSeconds);
    }
    public void Reset()
    {
        Deadline = null;
        RemainingSeconds = DurationSeconds;
        Started = false;
    }
}
