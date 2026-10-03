using Microsoft.Data.Sqlite;
using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Infrastructure;

public sealed partial class SqliteLocalStore(string databasePath) : ILocalStore, IHabitStore, IDailyStore, IPlanningStore, IGoalStore, IBodyStore, IWorkoutStore, IFocusStore, IDreamBoardStore, IDashboardStore, IJournalStore
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath),
        ForeignKeys = true,
        Pooling = false
    }.ToString();
    private readonly SemaphoreSlim initialization = new(1, 1);
    private bool initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await initialization.WaitAsync(cancellationToken);
        try
        {
            if (initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
            await using var connection = await OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (version > 11) throw new InvalidOperationException("Este banco pertence a uma versão mais recente do aplicativo. Atualize o app; seus dados foram preservados.");
            if (version == 0)
            {
                command.CommandText = """
                    CREATE TABLE user_profile (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        display_name TEXT NOT NULL,
                        theme INTEGER NOT NULL CHECK (theme BETWEEN 0 AND 2));
                    INSERT INTO user_profile VALUES (1, '', 0);
                    CREATE TABLE life_area (
                        id TEXT PRIMARY KEY NOT NULL,
                        name TEXT NOT NULL CHECK (length(trim(name)) BETWEEN 1 AND 60),
                        color_hex TEXT NOT NULL,
                        sort_order INTEGER NOT NULL CHECK (sort_order >= 0),
                        is_archived INTEGER NOT NULL CHECK (is_archived IN (0,1)));
                    PRAGMA user_version = 1;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version < 2)
            {
                command.CommandText = """
                    CREATE TABLE habit (
                        id TEXT PRIMARY KEY NOT NULL,
                        created_on TEXT NOT NULL,
                        archived_on TEXT);
                    CREATE TABLE habit_revision (
                        sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                        id TEXT UNIQUE NOT NULL,
                        habit_id TEXT NOT NULL REFERENCES habit(id),
                        name TEXT NOT NULL,
                        area_id TEXT REFERENCES life_area(id),
                        effective_from TEXT NOT NULL,
                        frequency INTEGER NOT NULL CHECK (frequency BETWEEN 0 AND 2),
                        weekday_mask INTEGER NOT NULL,
                        weekly_target INTEGER NOT NULL,
                        is_paused INTEGER NOT NULL CHECK (is_paused IN (0,1)));
                    CREATE INDEX idx_habit_revision ON habit_revision(habit_id,effective_from,sequence);
                    CREATE TABLE habit_event (
                        sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                        habit_id TEXT NOT NULL REFERENCES habit(id),
                        occurrence_date TEXT NOT NULL,
                        revision_id TEXT NOT NULL REFERENCES habit_revision(id),
                        is_completed INTEGER NOT NULL CHECK (is_completed IN (0,1)),
                        note TEXT,
                        recorded_at_utc TEXT NOT NULL);
                    CREATE INDEX idx_habit_event ON habit_event(habit_id,occurrence_date,sequence);
                    CREATE TABLE habit_status_event (
                        sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                        habit_id TEXT NOT NULL REFERENCES habit(id),
                        effective_on TEXT NOT NULL,
                        is_archived INTEGER NOT NULL CHECK (is_archived IN (0,1)));
                    CREATE INDEX idx_habit_status ON habit_status_event(habit_id,effective_on,sequence);
                    PRAGMA user_version = 2;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version < 3)
            {
                command.CommandText = """
                    CREATE TABLE daily_preferences (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        default_water_target_ml INTEGER,
                        mood_scale_max INTEGER NOT NULL CHECK (mood_scale_max IN (3,5,10)));
                    INSERT INTO daily_preferences VALUES (1,NULL,5);
                    CREATE TABLE daily_checkin_revision (
                        sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                        local_date TEXT NOT NULL,
                        woke_at TEXT,
                        sleep_minutes INTEGER,
                        sleep_rating INTEGER,
                        mood INTEGER,
                        mood_scale_max INTEGER NOT NULL CHECK (mood_scale_max IN (3,5,10)),
                        phone_minutes INTEGER,
                        note TEXT,
                        water_target_ml INTEGER,
                        recorded_at_utc TEXT NOT NULL);
                    CREATE INDEX idx_daily_checkin_date ON daily_checkin_revision(local_date,sequence);
                    CREATE TABLE water_event (
                        sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                        local_date TEXT NOT NULL,
                        delta_ml INTEGER NOT NULL CHECK (delta_ml != 0),
                        recorded_at_utc TEXT NOT NULL);
                    CREATE INDEX idx_water_date ON water_event(local_date,sequence);
                    CREATE TABLE daily_close_event (
                        sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                        local_date TEXT NOT NULL,
                        is_closed INTEGER NOT NULL CHECK (is_closed IN (0,1)),
                        habit_done INTEGER NOT NULL,
                        habit_planned INTEGER NOT NULL,
                        water_ml INTEGER NOT NULL,
                        water_target_ml INTEGER,
                        focus_minutes INTEGER,
                        workout_count INTEGER,
                        overall_rating INTEGER,
                        note TEXT,
                        recorded_at_utc TEXT NOT NULL);
                    CREATE INDEX idx_daily_close_date ON daily_close_event(local_date,sequence);
                    PRAGMA user_version = 3;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version < 4)
            {
                command.CommandText = """
                    CREATE TABLE planned_task (
                        id TEXT PRIMARY KEY NOT NULL,
                        title TEXT NOT NULL,
                        description TEXT,
                        due_on TEXT,
                        priority INTEGER NOT NULL CHECK (priority BETWEEN 0 AND 2),
                        area_id TEXT REFERENCES life_area(id),
                        reminder_at TEXT,
                        completed_at TEXT,
                        is_archived INTEGER NOT NULL CHECK (is_archived IN (0,1)));
                    CREATE INDEX idx_planned_task_due ON planned_task(due_on,is_archived);
                    CREATE TABLE appointment (
                        id TEXT PRIMARY KEY NOT NULL,
                        title TEXT NOT NULL,
                        starts_at TEXT NOT NULL,
                        duration_minutes INTEGER NOT NULL,
                        location TEXT,
                        note TEXT,
                        area_id TEXT REFERENCES life_area(id),
                        reminder_at TEXT,
                        is_archived INTEGER NOT NULL CHECK (is_archived IN (0,1)));
                    CREATE INDEX idx_appointment_starts ON appointment(starts_at,is_archived);
                    PRAGMA user_version = 4;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version < 5)
            {
                command.CommandText = """
                    CREATE TABLE goal (
                        id TEXT PRIMARY KEY NOT NULL,
                        title TEXT NOT NULL,
                        description TEXT,
                        starts_on TEXT NOT NULL,
                        due_on TEXT,
                        unit TEXT NOT NULL,
                        start_value TEXT,
                        target_value TEXT NOT NULL,
                        area_id TEXT REFERENCES life_area(id),
                        kind INTEGER NOT NULL CHECK (kind IN (0,1)),
                        completed_at TEXT,
                        is_archived INTEGER NOT NULL CHECK (is_archived IN (0,1)));
                    CREATE TABLE goal_progress_event (
                        sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                        goal_id TEXT NOT NULL REFERENCES goal(id),
                        value TEXT NOT NULL,
                        recorded_at TEXT NOT NULL,
                        note TEXT);
                    CREATE INDEX idx_goal_progress ON goal_progress_event(goal_id,sequence);
                    CREATE TABLE body_weight_record (
                        id TEXT PRIMARY KEY NOT NULL,
                        kilograms TEXT NOT NULL,
                        measured_at TEXT NOT NULL,
                        note TEXT);
                    CREATE INDEX idx_body_weight ON body_weight_record(measured_at,id);
                    CREATE TABLE goal_link (
                        goal_id TEXT NOT NULL REFERENCES goal(id),
                        kind INTEGER NOT NULL CHECK (kind IN (0,1)),
                        target_id TEXT NOT NULL,
                        PRIMARY KEY (goal_id,kind,target_id));
                    CREATE TABLE action_lane (
                        id TEXT PRIMARY KEY NOT NULL,
                        title TEXT NOT NULL,
                        sort_order INTEGER NOT NULL);
                    INSERT INTO action_lane VALUES ('start','Começar',0),('doing','Em progresso',1),('done','Concluídas',2);
                    CREATE TABLE action_card (
                        id TEXT PRIMARY KEY NOT NULL,
                        title TEXT NOT NULL,
                        description TEXT,
                        tags TEXT,
                        area_id TEXT REFERENCES life_area(id),
                        due_on TEXT,
                        goal_id TEXT REFERENCES goal(id),
                        lane_id TEXT NOT NULL REFERENCES action_lane(id),
                        is_archived INTEGER NOT NULL CHECK (is_archived IN (0,1)));
                    CREATE TABLE action_move_event (
                        sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                        card_id TEXT NOT NULL REFERENCES action_card(id),
                        lane_id TEXT NOT NULL REFERENCES action_lane(id),
                        recorded_at TEXT NOT NULL);
                    CREATE INDEX idx_action_move ON action_move_event(card_id,sequence);
                    PRAGMA user_version = 5;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version < 6)
            {
                command.CommandText = """
                    ALTER TABLE body_weight_record ADD COLUMN voided_at TEXT;
                    ALTER TABLE body_weight_record ADD COLUMN corrects_id TEXT REFERENCES body_weight_record(id);
                    CREATE TABLE body_measurement (
                        id TEXT PRIMARY KEY NOT NULL,
                        name TEXT NOT NULL,
                        value TEXT NOT NULL,
                        unit TEXT NOT NULL,
                        measured_at TEXT NOT NULL,
                        note TEXT,
                        voided_at TEXT,
                        corrects_id TEXT REFERENCES body_measurement(id));
                    CREATE INDEX idx_body_measurement ON body_measurement(name,measured_at,id);
                    PRAGMA user_version = 6;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version < 7)
            {
                command.CommandText = """
                    CREATE TABLE workout_plan_version (
                        id TEXT PRIMARY KEY NOT NULL,
                        plan_id TEXT NOT NULL,
                        version_number INTEGER NOT NULL,
                        name TEXT NOT NULL,
                        effective_from TEXT NOT NULL,
                        superseded_on TEXT,
                        UNIQUE(plan_id,version_number));
                    CREATE INDEX idx_workout_plan_current ON workout_plan_version(plan_id,effective_from,version_number);
                    CREATE TABLE workout_day (
                        id TEXT PRIMARY KEY NOT NULL,
                        version_id TEXT NOT NULL REFERENCES workout_plan_version(id),
                        weekday INTEGER NOT NULL CHECK (weekday BETWEEN 0 AND 6),
                        name TEXT NOT NULL,
                        sort_order INTEGER NOT NULL,
                        UNIQUE(version_id,weekday));
                    CREATE TABLE workout_exercise (
                        id TEXT PRIMARY KEY NOT NULL,
                        day_id TEXT NOT NULL REFERENCES workout_day(id),
                        name TEXT NOT NULL,
                        muscle_group TEXT NOT NULL,
                        note TEXT,
                        sort_order INTEGER NOT NULL,
                        target_sets INTEGER NOT NULL,
                        rep_min INTEGER NOT NULL,
                        rep_max INTEGER NOT NULL,
                        suggested_load_kg TEXT);
                    CREATE TABLE workout_cycle (
                        id TEXT PRIMARY KEY NOT NULL,
                        name TEXT NOT NULL,
                        started_on TEXT NOT NULL,
                        paused_on TEXT,
                        paused_days INTEGER NOT NULL DEFAULT 0,
                        archived_on TEXT);
                    CREATE TABLE workout_phase (
                        id TEXT PRIMARY KEY NOT NULL,
                        cycle_id TEXT NOT NULL REFERENCES workout_cycle(id),
                        name TEXT NOT NULL,
                        sort_order INTEGER NOT NULL,
                        weeks INTEGER NOT NULL,
                        target_sets INTEGER NOT NULL,
                        rep_min INTEGER NOT NULL,
                        rep_max INTEGER NOT NULL,
                        note TEXT);
                    CREATE TABLE workout_session (
                        id TEXT PRIMARY KEY NOT NULL,
                        version_id TEXT NOT NULL REFERENCES workout_plan_version(id),
                        day_id TEXT NOT NULL REFERENCES workout_day(id),
                        phase_id TEXT REFERENCES workout_phase(id),
                        started_at TEXT NOT NULL,
                        ended_at TEXT,
                        note TEXT);
                    CREATE INDEX idx_workout_session_start ON workout_session(started_at,id);
                    CREATE TABLE workout_set (
                        id TEXT PRIMARY KEY NOT NULL,
                        session_id TEXT NOT NULL REFERENCES workout_session(id),
                        exercise_id TEXT NOT NULL REFERENCES workout_exercise(id),
                        set_number INTEGER NOT NULL,
                        target_rep_min INTEGER NOT NULL,
                        target_rep_max INTEGER NOT NULL,
                        load_kg TEXT NOT NULL,
                        actual_reps INTEGER NOT NULL,
                        completed_at TEXT NOT NULL,
                        UNIQUE(session_id,exercise_id,set_number));
                    PRAGMA user_version = 7;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version < 8)
            {
                command.CommandText = """
                    CREATE TABLE focus_session (
                        id TEXT PRIMARY KEY NOT NULL,
                        title TEXT NOT NULL,
                        category TEXT NOT NULL,
                        target_minutes INTEGER NOT NULL,
                        started_at TEXT NOT NULL,
                        paused_at TEXT,
                        ended_at TEXT,
                        paused_minutes INTEGER NOT NULL DEFAULT 0,
                        rating INTEGER,
                        note TEXT,
                        area_id TEXT REFERENCES life_area(id));
                    CREATE INDEX idx_focus_session_start ON focus_session(started_at,id);
                    PRAGMA user_version = 8;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version < 9)
            {
                command.CommandText = """
                    CREATE TABLE dream_board_item (
                        id TEXT PRIMARY KEY NOT NULL,
                        image_path TEXT NOT NULL,
                        title TEXT,
                        description TEXT,
                        area_id TEXT REFERENCES life_area(id),
                        goal_id TEXT REFERENCES goal(id),
                        action_card_id TEXT REFERENCES action_card(id),
                        sort_order INTEGER NOT NULL,
                        size INTEGER NOT NULL CHECK (size BETWEEN 0 AND 2),
                        is_archived INTEGER NOT NULL CHECK (is_archived IN (0,1)));
                    CREATE INDEX idx_dream_board_order ON dream_board_item(is_archived,sort_order,id);
                    PRAGMA user_version = 9;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version < 10)
            {
                command.CommandText = """
                    CREATE TABLE dashboard_card_setting (key TEXT PRIMARY KEY NOT NULL, sort_order INTEGER NOT NULL, is_visible INTEGER NOT NULL CHECK (is_visible IN (0,1)));
                    INSERT INTO dashboard_card_setting VALUES ('habits',0,1),('planning',1,1),('workout',2,1),('water',3,1),('checkin',4,1);
                    PRAGMA user_version = 10;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version < 11)
            {
                command.CommandText = "CREATE TABLE journal_entry (id TEXT PRIMARY KEY NOT NULL, local_date TEXT NOT NULL, text TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT); CREATE INDEX idx_journal_date ON journal_entry(local_date,created_at); PRAGMA user_version = 11;";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            transaction.Commit();
            initialized = true;
        }
        finally { initialization.Release(); }
    }

    public async Task<UserProfile> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT display_name, theme FROM user_profile WHERE id = 1;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("Perfil local ausente.");
        return new UserProfile(reader.GetString(0), (ThemePreference)reader.GetInt32(1));
    }

    public async Task SaveProfileAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE user_profile SET display_name = $name, theme = $theme WHERE id = 1;";
        command.Parameters.AddWithValue("$name", profile.DisplayName);
        command.Parameters.AddWithValue("$theme", (int)profile.Theme);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LifeArea>> GetAreasAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, color_hex, sort_order, is_archived FROM life_area ORDER BY sort_order, name, id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<LifeArea>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new LifeArea(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetBoolean(4)));
        return result;
    }

    public async Task SaveAreaAsync(LifeArea area, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO life_area (id,name,color_hex,sort_order,is_archived)
            VALUES ($id,$name,$color,$order,$archived)
            ON CONFLICT(id) DO UPDATE SET name=$name,color_hex=$color,sort_order=$order,is_archived=$archived;
            """;
        command.Parameters.AddWithValue("$id", area.Id.ToString());
        command.Parameters.AddWithValue("$name", area.Name);
        command.Parameters.AddWithValue("$color", area.ColorHex);
        command.Parameters.AddWithValue("$order", area.SortOrder);
        command.Parameters.AddWithValue("$archived", area.IsArchived ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try { await connection.OpenAsync(cancellationToken); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
}
