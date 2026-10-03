using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class FoundationViewModel(ILocalStore store) : INotifyPropertyChanged
{
    private string name = "";
    private int theme;
    private string status = "";
    private bool busy;
    public string Name { get => name; set { name = value; Changed(); Changed(nameof(Greeting)); } }
    public int Theme { get => theme; set { theme = value; Changed(); } }
    public string Status { get => status; private set { status = value; Changed(); } }
    public bool Busy { get => busy; private set { busy = value; Changed(); Changed(nameof(Ready)); } }
    public bool Ready => !Busy;
    public string Greeting => string.IsNullOrWhiteSpace(Name) ? "Seu dia, no seu ritmo." : $"Olá, {Name}.";
    public ObservableCollection<LifeArea> Areas { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    public Task LoadAsync() => RunAsync(async () =>
    {
        var profile = await store.GetProfileAsync();
        Name = profile.DisplayName;
        Theme = (int)profile.Theme;
        ApplyTheme(profile.Theme);
        await RefreshAreasAsync();
    });
    public Task SaveProfileAsync() => RunAsync(async () =>
    {
        var profile = new UserProfile(Name, (ThemePreference)Theme);
        await store.SaveProfileAsync(profile);
        Name = profile.DisplayName;
        ApplyTheme(profile.Theme);
        Status = "Preferências salvas neste dispositivo.";
    });
    public Task SaveAreaAsync(Guid id, string title, string color, int order, bool archived = false) => RunAsync(async () =>
    {
        await store.SaveAreaAsync(new LifeArea(id, title, color, order, archived));
        await RefreshAreasAsync();
        Status = archived ? "Área arquivada. O histórico será preservado." : "Área salva.";
    });
    private async Task RefreshAreasAsync()
    {
        var areas = await store.GetAreasAsync();
        Areas.Clear();
        foreach (var area in areas) Areas.Add(area);
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (Busy) return;
        Busy = true;
        Status = "";
        try { await action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException)
        { Status = ex.Message; }
        catch (Exception) { Status = "Não foi possível acessar os dados locais. Tente novamente. Não limpe os dados do app."; }
        finally { Busy = false; }
    }
    private static void ApplyTheme(ThemePreference theme)
    {
        if (Microsoft.Maui.Controls.Application.Current is { } app)
            app.UserAppTheme = theme switch { ThemePreference.Light => AppTheme.Light, ThemePreference.System => AppTheme.Unspecified, _ => AppTheme.Dark };
    }
}
