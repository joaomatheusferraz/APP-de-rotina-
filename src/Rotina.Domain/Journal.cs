namespace Rotina.Domain;

public sealed record JournalEntry(Guid Id, DateOnly LocalDate, string Text, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt)
{
    public void Validate() { if (Id == Guid.Empty) throw new ArgumentException("Identificador inválido."); if (Text is null || string.IsNullOrWhiteSpace(Text) || Text.Length > 4000) throw new ArgumentException("A nota deve ter entre 1 e 4000 caracteres."); }
}
