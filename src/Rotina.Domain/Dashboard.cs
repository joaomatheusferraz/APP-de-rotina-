namespace Rotina.Domain;

public sealed record DashboardCardSetting(string Key, int SortOrder, bool IsVisible)
{
    public void Validate() { if (string.IsNullOrWhiteSpace(Key) || Key.Length > 40 || SortOrder < 0) throw new ArgumentException("Cartão inválido."); }
}
