namespace Rotina.Application;

// Occurrences will use local calendar dates; recorded instants use UTC DateTimeOffset.
public static class LocalDate
{
    public static DateOnly At(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
