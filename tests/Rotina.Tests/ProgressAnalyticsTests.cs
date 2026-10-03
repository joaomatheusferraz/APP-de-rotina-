using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Tests;

public sealed class ProgressAnalyticsTests
{
    [Fact]
    public void HabitPeriodComparesWithPreviousAndKeepsRestDistinct()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = today.AddDays(-13);
        var id = Guid.NewGuid();
        var revision = new HabitRevision(Guid.NewGuid(), id, "Estudar", null, start, HabitFrequency.Daily, 0, 0, false);
        var events = Enumerable.Range(0, 7).Select(i => new HabitEvent(i + 1, id, today.AddDays(-6 + i),
            revision.Id, true, null, DateTimeOffset.UtcNow)).ToList();
        var history = new HabitHistory(id, start, null, [revision], events);
        var report = ProgressAnalytics.Habits([history], today, 7);
        Assert.Equal(new HabitScore(7, 7), report.Current);
        Assert.Equal(new HabitScore(0, 7), report.Previous);
        Assert.Equal(1, Assert.Single(report.WeeklyConsistency).Value);
        var empty = ProgressAnalytics.Habits([], today, 7);
        Assert.Equal(HabitDayState.Rest, empty.Heatmap[today]);
        Assert.True(double.IsNaN(Assert.Single(empty.WeeklyConsistency).Value));
    }

    [Fact]
    public void BodyTrendsExcludeVoidedAndOutOfPeriodRecords()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var current = DateTimeOffset.Now;
        var weights = new[]
        {
            new BodyWeightRecord(Guid.NewGuid(), 60, current.AddDays(-40), null),
            new BodyWeightRecord(Guid.NewGuid(), 62, current.AddDays(-3), null, DateTimeOffset.UtcNow),
            new BodyWeightRecord(Guid.NewGuid(), 63, current.AddDays(-2), null),
            new BodyWeightRecord(Guid.NewGuid(), 64, current, null)
        };
        Assert.Equal(new[] { 63d, 64d }, ProgressAnalytics.Weights(weights, today, 30).Select(p => p.Value));
        var measurements = new[]
        {
            new BodyMeasurement(Guid.NewGuid(), "Cintura", 75, "cm", current.AddDays(-4), null),
            new BodyMeasurement(Guid.NewGuid(), "Peito", 95, "cm", current.AddDays(-2), null),
            new BodyMeasurement(Guid.NewGuid(), "Cintura", 74, "cm", current, null)
        };
        Assert.Equal(new[] { 75d, 74d }, ProgressAnalytics.Measurements(measurements, "cintura", "cm", today, 30).Select(p => p.Value));
    }
}
