namespace Rotina.Domain;

public sealed record BodyMeasurement
{
    public Guid Id { get; }
    public string Name { get; }
    public decimal Value { get; }
    public string Unit { get; }
    public DateTimeOffset MeasuredAt { get; }
    public string? Note { get; }
    public DateTimeOffset? VoidedAt { get; }
    public Guid? CorrectsId { get; }

    public BodyMeasurement(Guid id, string name, decimal value, string unit, DateTimeOffset measuredAt,
        string? note, DateTimeOffset? voidedAt = null, Guid? correctsId = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identificador inválido.");
        name = name.Trim(); unit = unit.Trim();
        if (name.Length is < 1 or > 80) throw new ArgumentException("Informe o nome da medida.");
        if (unit.Length is < 1 or > 20) throw new ArgumentException("Informe a unidade da medida.");
        if (value is <= 0 or > 100000) throw new ArgumentException("Valor de medida inválido.");
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > 1000) throw new ArgumentException("Nota muito longa.");
        Id = id; Name = name; Value = value; Unit = unit; MeasuredAt = measuredAt;
        Note = note; VoidedAt = voidedAt; CorrectsId = correctsId;
    }
}
