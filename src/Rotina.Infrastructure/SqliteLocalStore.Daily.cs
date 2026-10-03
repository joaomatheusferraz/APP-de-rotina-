using System.Globalization;
using Microsoft.Data.Sqlite;
using Rotina.Domain;

namespace Rotina.Infrastructure;

public sealed partial class SqliteLocalStore
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", Invariant);
    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", Invariant);
    private static string UtcText(DateTimeOffset instant) => instant.ToUniversalTime().ToString("O", Invariant);

    public async Task<DailyPreferences> GetDailyPreferencesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT default_water_target_ml,mood_scale_max FROM daily_preferences WHERE id=1;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("Preferências diárias ausentes.");
        return new DailyPreferences(reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.GetInt32(1));
    }

    public async Task SaveDailyPreferencesAsync(DailyPreferences preferences, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE daily_preferences SET default_water_target_ml=$water,mood_scale_max=$mood WHERE id=1;";
        command.Parameters.AddWithValue("$water", preferences.DefaultWaterTargetMl is null ? DBNull.Value : preferences.DefaultWaterTargetMl.Value);
        command.Parameters.AddWithValue("$mood", preferences.MoodScaleMax);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidDataException("Preferências diárias ausentes.");
    }

    public async Task<DailyRecord> GetDailyRecordAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var preferences = await GetDailyPreferencesAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        DailyCheckIn checkIn = new(date, waterTargetMl: preferences.DefaultWaterTargetMl, moodScaleMax: preferences.MoodScaleMax);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT woke_at,sleep_minutes,sleep_rating,mood,mood_scale_max,phone_minutes,note,water_target_ml
                FROM daily_checkin_revision WHERE local_date=$date ORDER BY sequence DESC LIMIT 1;
                """;
            command.Parameters.AddWithValue("$date", DateText(date));
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                checkIn = new DailyCheckIn(date,
                    reader.IsDBNull(0) ? null : TimeOnly.ParseExact(reader.GetString(0), "HH:mm", Invariant),
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7), reader.GetInt32(4));
        }
        var waterEvents = new List<WaterEvent>();
        long totalMl = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT sequence,delta_ml,recorded_at_utc FROM water_event WHERE local_date=$date ORDER BY sequence;";
            command.Parameters.AddWithValue("$date", DateText(date));
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var delta = reader.GetInt32(1);
                waterEvents.Add(new WaterEvent(reader.GetInt64(0), date, delta,
                    DateTimeOffset.Parse(reader.GetString(2), Invariant)));
                totalMl += delta;
            }
        }
        if (totalMl is < 0 or > int.MaxValue) throw new InvalidDataException("Histórico de água inválido.");
        var closures = new List<DailyClosure>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT sequence,is_closed,habit_done,habit_planned,water_ml,water_target_ml,focus_minutes,
                       workout_count,overall_rating,note,recorded_at_utc
                FROM daily_close_event WHERE local_date=$date ORDER BY sequence;
                """;
            command.Parameters.AddWithValue("$date", DateText(date));
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                closures.Add(new DailyClosure(reader.GetInt64(0), date, reader.GetBoolean(1),
                    reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    DateTimeOffset.Parse(reader.GetString(10), Invariant)));
        }
        return new DailyRecord(checkIn, (int)totalMl, waterEvents, closures);
    }

    public async Task<IReadOnlyList<DailyRecord>> GetDailyHistoryAsync(DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default)
    {
        if (through < from) throw new ArgumentException("Período inválido.");
        await InitializeAsync(cancellationToken);
        var dates = new List<DateOnly>();
        await using (var connection = await OpenAsync(cancellationToken))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT local_date FROM daily_checkin_revision UNION SELECT local_date FROM water_event
                UNION SELECT local_date FROM daily_close_event ORDER BY local_date DESC;
                """;
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var date = ParseDate(reader.GetString(0));
                if (date >= from && date <= through) dates.Add(date);
            }
        }
        var result = new List<DailyRecord>(dates.Count);
        foreach (var date in dates) result.Add(await GetDailyRecordAsync(date, cancellationToken));
        return result;
    }

    public async Task SaveCheckInAsync(DailyCheckIn checkIn, DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        CheckDateNotFuture(checkIn.Date, recordedAtUtc);
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO daily_checkin_revision(local_date,woke_at,sleep_minutes,sleep_rating,mood,mood_scale_max,
                phone_minutes,note,water_target_ml,recorded_at_utc)
            VALUES($date,$woke,$sleep,$rating,$mood,$scale,$phone,$note,$target,$recorded);
            """;
        command.Parameters.AddWithValue("$date", DateText(checkIn.Date));
        command.Parameters.AddWithValue("$woke", checkIn.WokeAt is null ? DBNull.Value : checkIn.WokeAt.Value.ToString("HH:mm", Invariant));
        command.Parameters.AddWithValue("$sleep", checkIn.SleepMinutes is null ? DBNull.Value : checkIn.SleepMinutes.Value);
        command.Parameters.AddWithValue("$rating", checkIn.SleepRating is null ? DBNull.Value : checkIn.SleepRating.Value);
        command.Parameters.AddWithValue("$mood", checkIn.Mood is null ? DBNull.Value : checkIn.Mood.Value);
        command.Parameters.AddWithValue("$scale", checkIn.MoodScaleMax);
        command.Parameters.AddWithValue("$phone", checkIn.PhoneMinutes is null ? DBNull.Value : checkIn.PhoneMinutes.Value);
        command.Parameters.AddWithValue("$note", checkIn.Note is null ? DBNull.Value : checkIn.Note);
        command.Parameters.AddWithValue("$target", checkIn.WaterTargetMl is null ? DBNull.Value : checkIn.WaterTargetMl.Value);
        command.Parameters.AddWithValue("$recorded", UtcText(recordedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddWaterAsync(DateOnly date, int deltaMl, DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        CheckDateNotFuture(date, recordedAtUtc);
        if (deltaMl == 0) throw new ArgumentException("O ajuste de água deve ser diferente de zero.");
        var current = await GetDailyRecordAsync(date, cancellationToken);
        var result = (long)current.WaterMl + deltaMl;
        if (result is < 0 or > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(deltaMl), "O consumo de água não pode ficar negativo.");
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM daily_checkin_revision WHERE local_date=$date;";
            command.Parameters.AddWithValue("$date", DateText(date));
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 0)
            {
                command.Parameters.Clear();
                command.CommandText = """
                    INSERT INTO daily_checkin_revision(local_date,mood_scale_max,water_target_ml,recorded_at_utc)
                    VALUES($date,$scale,$target,$recorded);
                    """;
                command.Parameters.AddWithValue("$date", DateText(date));
                command.Parameters.AddWithValue("$scale", current.CheckIn.MoodScaleMax);
                command.Parameters.AddWithValue("$target", current.CheckIn.WaterTargetMl is null ? DBNull.Value : current.CheckIn.WaterTargetMl.Value);
                command.Parameters.AddWithValue("$recorded", UtcText(recordedAtUtc));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO water_event(local_date,delta_ml,recorded_at_utc) VALUES($date,$delta,$recorded);";
            command.Parameters.AddWithValue("$date", DateText(date));
            command.Parameters.AddWithValue("$delta", deltaMl);
            command.Parameters.AddWithValue("$recorded", UtcText(recordedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }

    public async Task CloseDayAsync(DailyClosure closure, CancellationToken cancellationToken = default)
    {
        CheckDateNotFuture(closure.Date, closure.RecordedAtUtc);
        if (closure.HabitDone < 0 || closure.HabitPlanned < closure.HabitDone || closure.WaterMl < 0 ||
            closure.FocusMinutes is < 0 || closure.WorkoutCount is < 0 || closure.OverallRating is < 1 or > 5 ||
            closure.Note?.Length > 500)
            throw new ArgumentException("Resumo do dia inválido.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO daily_close_event(local_date,is_closed,habit_done,habit_planned,water_ml,water_target_ml,
                focus_minutes,workout_count,overall_rating,note,recorded_at_utc)
            VALUES($date,$closed,$done,$planned,$water,$target,$focus,$workout,$rating,$note,$recorded);
            """;
        command.Parameters.AddWithValue("$date", DateText(closure.Date));
        command.Parameters.AddWithValue("$closed", closure.IsClosed ? 1 : 0);
        command.Parameters.AddWithValue("$done", closure.HabitDone);
        command.Parameters.AddWithValue("$planned", closure.HabitPlanned);
        command.Parameters.AddWithValue("$water", closure.WaterMl);
        command.Parameters.AddWithValue("$target", closure.WaterTargetMl is null ? DBNull.Value : closure.WaterTargetMl.Value);
        command.Parameters.AddWithValue("$focus", closure.FocusMinutes is null ? DBNull.Value : closure.FocusMinutes.Value);
        command.Parameters.AddWithValue("$workout", closure.WorkoutCount is null ? DBNull.Value : closure.WorkoutCount.Value);
        command.Parameters.AddWithValue("$rating", closure.OverallRating is null ? DBNull.Value : closure.OverallRating.Value);
        command.Parameters.AddWithValue("$note", closure.Note is null ? DBNull.Value : closure.Note.Trim());
        command.Parameters.AddWithValue("$recorded", UtcText(closure.RecordedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void CheckDateNotFuture(DateOnly date, DateTimeOffset recordedAtUtc)
    {
        if (date > DateOnly.FromDateTime(recordedAtUtc.ToLocalTime().DateTime))
            throw new ArgumentException("Não é possível registrar dados diários no futuro.");
    }
}
