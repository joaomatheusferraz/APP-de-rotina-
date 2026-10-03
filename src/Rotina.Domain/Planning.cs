namespace Rotina.Domain;

public enum TaskPriority { Low, Normal, High }

public sealed record PlannedTask
{
    public Guid Id { get; }
    public string Title { get; }
    public string? Description { get; }
    public DateOnly? DueOn { get; }
    public TaskPriority Priority { get; }
    public Guid? AreaId { get; }
    public DateTimeOffset? ReminderAt { get; }
    public DateTimeOffset? CompletedAt { get; }
    public bool IsArchived { get; }

    public PlannedTask(Guid id, string title, string? description, DateOnly? dueOn, TaskPriority priority,
        Guid? areaId, DateTimeOffset? reminderAt, DateTimeOffset? completedAt = null, bool isArchived = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identificador inválido.");
        title = title.Trim();
        if (title.Length is < 1 or > 120) throw new ArgumentException("O título deve ter entre 1 e 120 caracteres.");
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (description?.Length > 2000) throw new ArgumentException("Descrição muito longa.");
        if (reminderAt is not null && dueOn is null) throw new ArgumentException("Defina um prazo para usar lembrete.");
        Id = id; Title = title; Description = description; DueOn = dueOn; Priority = priority;
        AreaId = areaId; ReminderAt = reminderAt; CompletedAt = completedAt; IsArchived = isArchived;
    }
}

public sealed record Appointment
{
    public Guid Id { get; }
    public string Title { get; }
    public DateTimeOffset StartsAt { get; }
    public int DurationMinutes { get; }
    public string? Location { get; }
    public string? Note { get; }
    public Guid? AreaId { get; }
    public DateTimeOffset? ReminderAt { get; }
    public bool IsArchived { get; }

    public Appointment(Guid id, string title, DateTimeOffset startsAt, int durationMinutes,
        string? location, string? note, Guid? areaId, DateTimeOffset? reminderAt, bool isArchived = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identificador inválido.");
        title = title.Trim();
        if (title.Length is < 1 or > 120) throw new ArgumentException("O título deve ter entre 1 e 120 caracteres.");
        if (durationMinutes is < 1 or > 10080) throw new ArgumentException("A duração deve ficar entre 1 minuto e 7 dias.");
        location = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (location?.Length > 300 || note?.Length > 2000) throw new ArgumentException("Local ou nota muito longa.");
        if (reminderAt > startsAt) throw new ArgumentException("O lembrete não pode vir depois do compromisso.");
        Id = id; Title = title; StartsAt = startsAt; DurationMinutes = durationMinutes;
        Location = location; Note = note; AreaId = areaId; ReminderAt = reminderAt; IsArchived = isArchived;
    }
}
