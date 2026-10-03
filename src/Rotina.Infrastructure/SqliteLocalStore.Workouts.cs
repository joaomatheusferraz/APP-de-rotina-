using Microsoft.Data.Sqlite;
using Rotina.Domain;

namespace Rotina.Infrastructure;

public sealed partial class SqliteLocalStore
{
    public async Task<IReadOnlyList<WorkoutPlanVersion>> GetWorkoutPlansAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var plans = new List<(Guid Id, Guid PlanId, int Number, string Name, DateOnly From, DateOnly? Until)>();
        var days = new Dictionary<Guid, List<(Guid Id, DayOfWeek Weekday, string Name, int Order)>>();
        var exercises = new Dictionary<Guid, List<WorkoutExercise>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,plan_id,version_number,name,effective_from,superseded_on FROM workout_plan_version ORDER BY effective_from,version_number;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) plans.Add((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                reader.GetInt32(2), reader.GetString(3), DateOnly.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : DateOnly.Parse(reader.GetString(5))));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,version_id,weekday,name,sort_order FROM workout_day ORDER BY sort_order,id;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var versionId = Guid.Parse(reader.GetString(1));
                if (!days.TryGetValue(versionId, out var list)) days[versionId] = list = [];
                list.Add((Guid.Parse(reader.GetString(0)), (DayOfWeek)reader.GetInt32(2), reader.GetString(3), reader.GetInt32(4)));
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,day_id,name,muscle_group,note,sort_order,target_sets,rep_min,rep_max,suggested_load_kg FROM workout_exercise ORDER BY sort_order,id;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var dayId = Guid.Parse(reader.GetString(1));
                if (!exercises.TryGetValue(dayId, out var list)) exercises[dayId] = list = [];
                list.Add(new WorkoutExercise(Guid.Parse(reader.GetString(0)), reader.GetString(2), reader.GetString(3), TextOrNull(reader, 4),
                    reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.IsDBNull(9) ? null : ReadDecimal(reader, 9)));
            }
        }
        return plans.Select(p => new WorkoutPlanVersion(p.Id, p.PlanId, p.Number, p.Name, p.From, p.Until,
            (days.TryGetValue(p.Id, out var list) ? list : [])
                .Select(d => new WorkoutDay(d.Id, d.Weekday, d.Name, d.Order,
                    exercises.TryGetValue(d.Id, out var sets) ? sets : [])).ToList())).ToList();
    }

    public async Task AddWorkoutPlanVersionAsync(WorkoutPlanVersion version, CancellationToken cancellationToken = default)
    {
        version.Validate();
        if (version.SupersededOn is not null || version.EffectiveFrom > DateOnly.FromDateTime(DateTime.Today))
            throw new ArgumentException("A nova versão deve começar hoje ou antes.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var latest = connection.CreateCommand();
        latest.Transaction = transaction;
        latest.CommandText = "SELECT id,version_number,effective_from FROM workout_plan_version WHERE plan_id=$plan ORDER BY version_number DESC LIMIT 1;";
        latest.Parameters.AddWithValue("$plan", version.PlanId.ToString());
        using (var reader = await latest.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                if (version.Number != reader.GetInt32(1) + 1 || version.EffectiveFrom < DateOnly.Parse(reader.GetString(2)))
                    throw new ArgumentException("A versão precisa suceder o plano anterior.");
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE workout_plan_version SET superseded_on=$on WHERE id=$id;";
                update.Parameters.AddWithValue("$on", version.EffectiveFrom.ToString("yyyy-MM-dd"));
                update.Parameters.AddWithValue("$id", reader.GetString(0));
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (version.Number != 1) throw new ArgumentException("O primeiro plano deve ter versão 1.");
        }
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO workout_plan_version VALUES ($id,$plan,$number,$name,$from,NULL);";
            insert.Parameters.AddWithValue("$id", version.Id.ToString());
            insert.Parameters.AddWithValue("$plan", version.PlanId.ToString());
            insert.Parameters.AddWithValue("$number", version.Number);
            insert.Parameters.AddWithValue("$name", version.Name.Trim());
            insert.Parameters.AddWithValue("$from", version.EffectiveFrom.ToString("yyyy-MM-dd"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var day in version.Days)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO workout_day VALUES ($id,$version,$weekday,$name,$order);";
            insert.Parameters.AddWithValue("$id", day.Id.ToString());
            insert.Parameters.AddWithValue("$version", version.Id.ToString());
            insert.Parameters.AddWithValue("$weekday", (int)day.Weekday);
            insert.Parameters.AddWithValue("$name", day.Name.Trim());
            insert.Parameters.AddWithValue("$order", day.Order);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            foreach (var exercise in day.Exercises)
            {
                using var exerciseInsert = connection.CreateCommand();
                exerciseInsert.Transaction = transaction;
                exerciseInsert.CommandText = "INSERT INTO workout_exercise VALUES ($id,$day,$name,$muscle,$note,$order,$sets,$min,$max,$load);";
                exerciseInsert.Parameters.AddWithValue("$id", exercise.Id.ToString());
                exerciseInsert.Parameters.AddWithValue("$day", day.Id.ToString());
                exerciseInsert.Parameters.AddWithValue("$name", exercise.Name.Trim());
                exerciseInsert.Parameters.AddWithValue("$muscle", exercise.MuscleGroup.Trim());
                exerciseInsert.Parameters.AddWithValue("$note", Db(exercise.Note));
                exerciseInsert.Parameters.AddWithValue("$order", exercise.Order);
                exerciseInsert.Parameters.AddWithValue("$sets", exercise.TargetSets);
                exerciseInsert.Parameters.AddWithValue("$min", exercise.RepMin);
                exerciseInsert.Parameters.AddWithValue("$max", exercise.RepMax);
                exerciseInsert.Parameters.AddWithValue("$load", Db(exercise.SuggestedLoadKg is { } load ? DecimalText(load) : null));
                await exerciseInsert.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        transaction.Commit();
    }

    public async Task<IReadOnlyList<WorkoutSession>> GetWorkoutSessionsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var sessions = new List<(Guid Id, Guid Version, Guid Day, Guid? Phase, DateTimeOffset Start, DateTimeOffset? End, string? Note)>();
        var sets = new Dictionary<Guid, List<WorkoutSet>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,version_id,day_id,phase_id,started_at,ended_at,note FROM workout_session ORDER BY started_at,id;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) sessions.Add((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)), GuidOrNull(reader, 3), DateTimeOffset.Parse(reader.GetString(4)),
                InstantOrNull(reader, 5), TextOrNull(reader, 6)));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,session_id,exercise_id,set_number,target_rep_min,target_rep_max,load_kg,actual_reps,completed_at FROM workout_set ORDER BY completed_at,set_number;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var sessionId = Guid.Parse(reader.GetString(1));
                if (!sets.TryGetValue(sessionId, out var list)) sets[sessionId] = list = [];
                list.Add(new WorkoutSet(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(2)), reader.GetInt32(3),
                    reader.GetInt32(4), reader.GetInt32(5), ReadDecimal(reader, 6), reader.GetInt32(7), DateTimeOffset.Parse(reader.GetString(8))));
            }
        }
        return sessions.Select(s => new WorkoutSession(s.Id, s.Version, s.Day, s.Phase, s.Start, s.End, s.Note,
            sets.TryGetValue(s.Id, out var list) ? list : [])).ToList();
    }

    public async Task StartWorkoutAsync(WorkoutSession session, CancellationToken cancellationToken = default)
    {
        if (session.Id == Guid.Empty || session.EndedAt is not null || session.Sets.Count != 0 ||
            session.StartedAt > DateTimeOffset.UtcNow.AddMinutes(1)) throw new ArgumentException("Sessão inicial inválida.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workout_session (id,version_id,day_id,phase_id,started_at,ended_at,note)
            SELECT $id,v.id,d.id,$phase,$start,NULL,NULL FROM workout_plan_version v
            JOIN workout_day d ON d.version_id=v.id WHERE v.id=$version AND d.id=$day
            AND NOT EXISTS (SELECT 1 FROM workout_session WHERE ended_at IS NULL);
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString());
        command.Parameters.AddWithValue("$version", session.PlanVersionId.ToString());
        command.Parameters.AddWithValue("$day", session.DayId.ToString());
        command.Parameters.AddWithValue("$phase", Db(session.PhaseId?.ToString()));
        command.Parameters.AddWithValue("$start", session.StartedAt.ToUniversalTime().ToString("O"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Plano inválido ou já existe treino em andamento.");
    }

    public async Task AddWorkoutSetAsync(Guid sessionId, WorkoutSet set, CancellationToken cancellationToken = default)
    {
        set.Validate();
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workout_set (id,session_id,exercise_id,set_number,target_rep_min,target_rep_max,load_kg,actual_reps,completed_at)
            SELECT $id,s.id,e.id,$number,$min,$max,$load,$reps,$at FROM workout_session s
            JOIN workout_exercise e ON e.day_id=s.day_id WHERE s.id=$session AND s.ended_at IS NULL
            AND e.id=$exercise AND $at>=s.started_at;
            """;
        command.Parameters.AddWithValue("$id", set.Id.ToString());
        command.Parameters.AddWithValue("$session", sessionId.ToString());
        command.Parameters.AddWithValue("$exercise", set.ExerciseId.ToString());
        command.Parameters.AddWithValue("$number", set.Number);
        command.Parameters.AddWithValue("$min", set.TargetRepMin);
        command.Parameters.AddWithValue("$max", set.TargetRepMax);
        command.Parameters.AddWithValue("$load", DecimalText(set.LoadKg));
        command.Parameters.AddWithValue("$reps", set.ActualReps);
        command.Parameters.AddWithValue("$at", set.CompletedAt.ToUniversalTime().ToString("O"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Série não pertence ao treino em andamento.");
    }

    public async Task FinishWorkoutAsync(Guid sessionId, DateTimeOffset endedAt, string? note, CancellationToken cancellationToken = default)
    {
        if (endedAt > DateTimeOffset.UtcNow.AddMinutes(1) || note?.Length > 1000) throw new ArgumentException("Fim de treino inválido.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE workout_session SET ended_at=$end,note=$note WHERE id=$id AND ended_at IS NULL AND started_at<=$end;";
        command.Parameters.AddWithValue("$id", sessionId.ToString());
        command.Parameters.AddWithValue("$end", endedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$note", Db(note));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Treino não está em andamento.");
    }

    public async Task<IReadOnlyList<WorkoutCycle>> GetWorkoutCyclesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var cycles = new List<(Guid Id, string Name, DateOnly Start, DateOnly? Pause, int PausedDays, DateOnly? Archive)>();
        var phases = new Dictionary<Guid, List<WorkoutPhase>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,name,started_on,paused_on,paused_days,archived_on FROM workout_cycle ORDER BY started_on,id;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) cycles.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1),
                DateOnly.Parse(reader.GetString(2)), reader.IsDBNull(3) ? null : DateOnly.Parse(reader.GetString(3)),
                reader.GetInt32(4), reader.IsDBNull(5) ? null : DateOnly.Parse(reader.GetString(5))));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,cycle_id,name,sort_order,weeks,target_sets,rep_min,rep_max,note FROM workout_phase ORDER BY sort_order,id;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var cycleId = Guid.Parse(reader.GetString(1));
                if (!phases.TryGetValue(cycleId, out var list)) phases[cycleId] = list = [];
                list.Add(new WorkoutPhase(Guid.Parse(reader.GetString(0)), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4),
                    reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), TextOrNull(reader, 8)));
            }
        }
        return cycles.Select(c => new WorkoutCycle(c.Id, c.Name, c.Start, c.Pause, c.PausedDays, c.Archive,
            phases.TryGetValue(c.Id, out var list) ? list : [])).ToList();
    }

    public async Task AddWorkoutCycleAsync(WorkoutCycle cycle, CancellationToken cancellationToken = default)
    {
        cycle.Validate();
        if (cycle.PausedOn is not null || cycle.PausedDays != 0 || cycle.ArchivedOn is not null ||
            cycle.StartedOn > DateOnly.FromDateTime(DateTime.Today)) throw new ArgumentException("Novo ciclo inválido.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using (var archive = connection.CreateCommand())
        {
            archive.Transaction = transaction;
            archive.CommandText = "UPDATE workout_cycle SET archived_on=$today WHERE archived_on IS NULL;";
            archive.Parameters.AddWithValue("$today", cycle.StartedOn.ToString("yyyy-MM-dd"));
            await archive.ExecuteNonQueryAsync(cancellationToken);
        }
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO workout_cycle VALUES ($id,$name,$start,NULL,0,NULL);";
            insert.Parameters.AddWithValue("$id", cycle.Id.ToString());
            insert.Parameters.AddWithValue("$name", cycle.Name.Trim());
            insert.Parameters.AddWithValue("$start", cycle.StartedOn.ToString("yyyy-MM-dd"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var phase in cycle.Phases)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO workout_phase VALUES ($id,$cycle,$name,$order,$weeks,$sets,$min,$max,$note);";
            insert.Parameters.AddWithValue("$id", phase.Id.ToString());
            insert.Parameters.AddWithValue("$cycle", cycle.Id.ToString());
            insert.Parameters.AddWithValue("$name", phase.Name.Trim());
            insert.Parameters.AddWithValue("$order", phase.Order);
            insert.Parameters.AddWithValue("$weeks", phase.Weeks);
            insert.Parameters.AddWithValue("$sets", phase.TargetSets);
            insert.Parameters.AddWithValue("$min", phase.RepMin);
            insert.Parameters.AddWithValue("$max", phase.RepMax);
            insert.Parameters.AddWithValue("$note", Db(phase.Note));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }

    public async Task PauseWorkoutCycleAsync(Guid id, DateOnly pausedOn, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE workout_cycle SET paused_on=$on WHERE id=$id AND paused_on IS NULL AND archived_on IS NULL AND started_on<=$on;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$on", pausedOn.ToString("yyyy-MM-dd"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Ciclo não está ativo.");
    }

    public async Task ResumeWorkoutCycleAsync(Guid id, DateOnly resumedOn, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE workout_cycle SET paused_days=paused_days+(julianday($on)-julianday(paused_on)),paused_on=NULL WHERE id=$id AND paused_on IS NOT NULL AND archived_on IS NULL AND paused_on<=$on;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$on", resumedOn.ToString("yyyy-MM-dd"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Ciclo não está pausado.");
    }
}
