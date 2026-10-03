using Rotina.Domain;

namespace Rotina.Application;

public sealed record TrendPoint(DateOnly Date, double Value);
public sealed record HabitPeriodReport(HabitScore Current, HabitScore Previous,
    IReadOnlyList<TrendPoint> WeeklyConsistency, IReadOnlyDictionary<DateOnly, HabitDayState> Heatmap);

public static class ProgressAnalytics
{
    public static HabitPeriodReport Habits(IReadOnlyList<HabitHistory> histories, DateOnly today, int days)
    {
        if (days is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(days));
        var current = new HabitScore(0, 0);
        var previous = new HabitScore(0, 0);
        foreach (var history in histories)
        {
            var now = HabitAnalytics.Score(history, today, days);
            var before = HabitAnalytics.Score(history, today.AddDays(-days), days);
            current = new HabitScore(current.Done + now.Done, current.Planned + now.Planned);
            previous = new HabitScore(previous.Done + before.Done, previous.Planned + before.Planned);
        }
        var start = today.AddDays(1 - days);
        var trend = new List<TrendPoint>();
        for (var cursor = start; cursor <= today; cursor = cursor.AddDays(7))
        {
            var end = cursor.AddDays(6) < today ? cursor.AddDays(6) : today;
            var length = end.DayNumber - cursor.DayNumber + 1;
            var scores = histories.Select(h => HabitAnalytics.Score(h, end, length)).ToList();
            var done = scores.Sum(s => s.Done);
            var planned = scores.Sum(s => s.Planned);
            trend.Add(new TrendPoint(end, planned == 0 ? double.NaN : (double)done / planned));
        }
        var heatmap = new Dictionary<DateOnly, HabitDayState>();
        for (var date = start; date <= today; date = date.AddDays(1))
        {
            var states = histories.Select(h => HabitAnalytics.Day(h, date, today)).ToList();
            heatmap[date] = states.Contains(HabitDayState.Missed) ? HabitDayState.Missed :
                states.Contains(HabitDayState.ScheduledPending) ? HabitDayState.ScheduledPending :
                states.Contains(HabitDayState.Completed) ? HabitDayState.Completed : HabitDayState.Rest;
        }
        return new HabitPeriodReport(current, previous, trend, heatmap);
    }

    public static IReadOnlyList<TrendPoint> Weights(IEnumerable<BodyWeightRecord> records, DateOnly today, int days) =>
        records.Where(r => r.VoidedAt is null && DateOnly.FromDateTime(r.MeasuredAt.ToLocalTime().DateTime) >= today.AddDays(1 - days)
            && DateOnly.FromDateTime(r.MeasuredAt.ToLocalTime().DateTime) <= today)
            .OrderBy(r => r.MeasuredAt).ThenBy(r => r.Id)
            .Select(r => new TrendPoint(DateOnly.FromDateTime(r.MeasuredAt.ToLocalTime().DateTime), (double)r.Kilograms)).ToList();

    public static IReadOnlyList<TrendPoint> Measurements(IEnumerable<BodyMeasurement> records, string name, string unit,
        DateOnly today, int days) => records
            .Where(r => r.VoidedAt is null && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && r.Unit == unit &&
                DateOnly.FromDateTime(r.MeasuredAt.ToLocalTime().DateTime) >= today.AddDays(1 - days) &&
                DateOnly.FromDateTime(r.MeasuredAt.ToLocalTime().DateTime) <= today)
            .OrderBy(r => r.MeasuredAt).ThenBy(r => r.Id)
            .Select(r => new TrendPoint(DateOnly.FromDateTime(r.MeasuredAt.ToLocalTime().DateTime), (double)r.Value)).ToList();
}
