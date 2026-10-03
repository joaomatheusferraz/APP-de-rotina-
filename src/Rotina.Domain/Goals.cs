namespace Rotina.Domain;

public enum GoalKind { Numeric, BodyWeight }
public enum GoalLinkKind { Habit, Task }

public sealed record Goal
{
    public Guid Id { get; }
    public string Title { get; }
    public string? Description { get; }
    public DateOnly StartsOn { get; }
    public DateOnly? DueOn { get; }
    public string Unit { get; }
    public decimal? StartValue { get; }
    public decimal TargetValue { get; }
    public Guid? AreaId { get; }
    public GoalKind Kind { get; }
    public DateTimeOffset? CompletedAt { get; }
    public bool IsArchived { get; }

    public Goal(Guid id, string title, string? description, DateOnly startsOn, DateOnly? dueOn,
        string unit, decimal? startValue, decimal targetValue, Guid? areaId, GoalKind kind,
        DateTimeOffset? completedAt = null, bool isArchived = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identificador inválido.");
        title = title.Trim();
        if (title.Length is < 1 or > 120) throw new ArgumentException("O título da meta deve ter entre 1 e 120 caracteres.");
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (description?.Length > 2000) throw new ArgumentException("Descrição muito longa.");
        if (dueOn < startsOn) throw new ArgumentException("O prazo não pode anteceder o início.");
        unit = unit.Trim();
        if (unit.Length is < 1 or > 30) throw new ArgumentException("Informe uma unidade de progresso.");
        if (targetValue <= 0 || targetValue > 1000000000) throw new ArgumentException("Valor-alvo inválido.");
        if (startValue is < 0 or > 1000000000) throw new ArgumentException("Valor inicial inválido.");
        if (startValue == targetValue) throw new ArgumentException("O valor inicial deve ser diferente do alvo.");
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == GoalKind.BodyWeight && unit != "kg") throw new ArgumentException("Meta de peso deve usar kg.");
        Id = id; Title = title; Description = description; StartsOn = startsOn; DueOn = dueOn;
        Unit = unit; StartValue = startValue; TargetValue = targetValue; AreaId = areaId;
        Kind = kind; CompletedAt = completedAt; IsArchived = isArchived;
    }

    public double? Progress(decimal? current)
    {
        if (current is null) return null;
        if (Kind == GoalKind.BodyWeight && StartValue is null) return null;
        var baseline = StartValue ?? 0;
        var span = TargetValue - baseline;
        if (span == 0) return null;
        return Math.Clamp((double)((current.Value - baseline) / span), 0, 1);
    }
}

public sealed record GoalProgressEvent(long Sequence, Guid GoalId, decimal Value,
    DateTimeOffset RecordedAt, string? Note);
public sealed record BodyWeightRecord
{
    public Guid Id { get; }
    public decimal Kilograms { get; }
    public DateTimeOffset MeasuredAt { get; }
    public string? Note { get; }
    public DateTimeOffset? VoidedAt { get; }
    public Guid? CorrectsId { get; }
    public BodyWeightRecord(Guid id, decimal kilograms, DateTimeOffset measuredAt, string? note,
        DateTimeOffset? voidedAt = null, Guid? correctsId = null)
    {
        if (id == Guid.Empty || kilograms is < 1 or > 500) throw new ArgumentException("Peso inválido.");
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > 1000) throw new ArgumentException("Nota muito longa.");
        Id = id; Kilograms = kilograms; MeasuredAt = measuredAt; Note = note;
        VoidedAt = voidedAt; CorrectsId = correctsId;
    }
}
public sealed record GoalLink(Guid GoalId, GoalLinkKind Kind, Guid TargetId);

public sealed record ActionLane(string Id, string Title, int SortOrder);
public sealed record ActionCard
{
    public Guid Id { get; }
    public string Title { get; }
    public string? Description { get; }
    public string? Tags { get; }
    public Guid? AreaId { get; }
    public DateOnly? DueOn { get; }
    public Guid? GoalId { get; }
    public string LaneId { get; }
    public bool IsArchived { get; }

    public ActionCard(Guid id, string title, string? description, string? tags, Guid? areaId,
        DateOnly? dueOn, Guid? goalId, string laneId, bool isArchived = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identificador inválido.");
        title = title.Trim(); laneId = laneId.Trim();
        if (title.Length is < 1 or > 120 || laneId.Length is < 1 or > 40)
            throw new ArgumentException("Título ou coluna inválida.");
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim();
        if (description?.Length > 2000 || tags?.Length > 300) throw new ArgumentException("Texto muito longo.");
        Id = id; Title = title; Description = description; Tags = tags; AreaId = areaId;
        DueOn = dueOn; GoalId = goalId; LaneId = laneId; IsArchived = isArchived;
    }
}
public sealed record ActionMoveEvent(long Sequence, Guid CardId, string LaneId, DateTimeOffset RecordedAt);
