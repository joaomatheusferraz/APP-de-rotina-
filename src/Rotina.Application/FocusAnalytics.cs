using Rotina.Domain;

namespace Rotina.Application;

public sealed record FocusCategoryStat(string Category, int Sessions, int Minutes);

public static class FocusAnalytics
{
    public static IReadOnlyList<FocusCategoryStat> ByCategory(IEnumerable<FocusSession> sessions, DateOnly today, int days)
    {
        if (days is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(days));
        var start = today.AddDays(1 - days);
        return sessions.Where(s => s.EndedAt is not null &&
                DateOnly.FromDateTime(s.StartedAt.ToLocalTime().DateTime) >= start &&
                DateOnly.FromDateTime(s.StartedAt.ToLocalTime().DateTime) <= today)
            .GroupBy(s => s.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FocusCategoryStat(g.Key, g.Count(), g.Sum(s => s.ElapsedMinutes(s.EndedAt!.Value))))
            .OrderByDescending(s => s.Minutes).ThenBy(s => s.Category).ToList();
    }
}
