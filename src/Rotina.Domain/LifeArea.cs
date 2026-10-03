namespace Rotina.Domain;

// Archival retains the identity referenced by historical records.
public sealed record LifeArea
{
    public Guid Id { get; }
    public string Name { get; }
    public string ColorHex { get; }
    public int SortOrder { get; }
    public bool IsArchived { get; }

    public LifeArea(Guid id, string name, string colorHex, int sortOrder, bool isArchived = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("A área precisa de um identificador.");
        name = name.Trim();
        if (name.Length is < 1 or > 60) throw new ArgumentException("O nome deve ter entre 1 e 60 caracteres.");
        if (colorHex.Length != 7 || colorHex[0] != '#' || !colorHex[1..].All(Uri.IsHexDigit))
            throw new ArgumentException("Escolha uma cor no formato #RRGGBB.");
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        Id = id;
        Name = name;
        ColorHex = colorHex.ToUpperInvariant();
        SortOrder = sortOrder;
        IsArchived = isArchived;
    }
}
