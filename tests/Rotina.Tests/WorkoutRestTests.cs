using System.Text.Json;
using Rotina.Domain;

namespace Rotina.Tests;

public sealed class WorkoutRestTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PauseFreezesTimeAndResumeUsesRemainingDuration()
    {
        var rest = new WorkoutRest();
        rest.Start(Now);
        rest.Pause(Now.AddSeconds(25));
        Assert.Equal(65, rest.Remaining(Now.AddMinutes(5)));
        rest.Resume(Now.AddMinutes(5));
        Assert.Equal(55, rest.Remaining(Now.AddMinutes(5).AddSeconds(10)));
    }

    [Fact]
    public void RestoredDeadlineAccountsForTimeAwayAndNeverGoesNegative()
    {
        var rest = new WorkoutRest();
        rest.Start(Now);
        var restored = JsonSerializer.Deserialize<WorkoutRest>(JsonSerializer.Serialize(rest))!;
        Assert.Equal(30, restored.Remaining(Now.AddSeconds(60)));
        Assert.Equal(0, restored.Remaining(Now.AddMinutes(10)));
    }

    [Fact]
    public void ExtendAndRestartDoNotReusePreviousDeadline()
    {
        var rest = new WorkoutRest();
        rest.Start(Now);
        rest.Extend(Now.AddSeconds(30));
        Assert.Equal(75, rest.Remaining(Now.AddSeconds(30)));
        rest.Pause(Now.AddSeconds(30));
        rest.Extend(Now.AddMinutes(2));
        Assert.Null(rest.Deadline);
        Assert.Equal(90, rest.Remaining(Now.AddMinutes(3)));
        rest.DurationSeconds = 120;
        rest.Reset();
        Assert.False(rest.Started);
        rest.Start(Now.AddMinutes(5));
        Assert.Equal(120, rest.Remaining(Now.AddMinutes(5)));
    }
}
