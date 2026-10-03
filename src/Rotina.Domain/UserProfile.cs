namespace Rotina.Domain;

public enum ThemePreference { Dark, Light, System }

public sealed record UserProfile
{
    public string DisplayName { get; }
    public ThemePreference Theme { get; }

    public UserProfile(string displayName, ThemePreference theme)
    {
        var name = displayName.Trim();
        if (name.Length > 60) throw new ArgumentException("Use até 60 caracteres para o nome.");
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        DisplayName = name;
        Theme = theme;
    }

    public static UserProfile Default => new("", ThemePreference.Dark);
}
