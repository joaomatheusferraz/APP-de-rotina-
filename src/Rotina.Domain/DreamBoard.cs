namespace Rotina.Domain;

public enum DreamCardSize { Small, Medium, Large }

public sealed record DreamBoardItem(Guid Id, string ImagePath, string? Title, string? Description,
    Guid? AreaId, Guid? GoalId, Guid? ActionCardId, int SortOrder, DreamCardSize Size, bool IsArchived = false)
{
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(ImagePath) || ImagePath.Length > 500 ||
            Title?.Length > 120 || Description?.Length > 2000 || SortOrder < 0 ||
            !Enum.IsDefined(Size)) throw new ArgumentException("Cartão do Quadro dos Sonhos inválido.");
    }
}
