using System.Text.Json;
using Rotina.Domain;

namespace Rotina.Mobile;

internal sealed class WorkoutRestPanel : Border
{
    private readonly string key;
    private readonly WorkoutRest rest;
    private readonly Label clock = new() { FontSize = 40, FontFamily = "OpenSansSemibold", TextColor = Colors.White };
    private readonly Label status = new() { FontSize = 13, TextColor = Color.FromArgb("#BCC9E4") };
    private readonly Button toggle = new();
    private readonly ProgressBar progress = new() { ProgressColor = Color.FromArgb("#66DAEB"), BackgroundColor = Color.FromArgb("#2B3C5B") };
    private readonly List<Button> presets = [];
    private readonly Microsoft.Maui.Dispatching.IDispatcherTimer ticker;

    public WorkoutRestPanel(Guid sessionId)
    {
        key = $"workout-rest-{sessionId}";
        try { rest = JsonSerializer.Deserialize<WorkoutRest>(Preferences.Default.Get(key, "{}")) ?? new(); }
        catch (JsonException) { rest = new(); }
        BackgroundColor = Color.FromArgb("#14253D");
        Stroke = Color.FromArgb("#304465");
        Margin = new Thickness(16, 0, 16, 12);
        Padding = 16;
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 12 };
        header.Add(new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center,
            Children = { new Label { Text = "DESCANSO", FontSize = 11, CharacterSpacing = 2, TextColor = Color.FromArgb("#66DAEB") }, status } });
        header.Add(clock, 1);
        var choices = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 8 };
        foreach (var seconds in new[] { 60, 90, 120 })
        {
            var button = new Button { Text = $"{seconds} s", FontSize = 13, Padding = 6 };
            button.Clicked += (_, _) => { rest.DurationSeconds = seconds; rest.Reset(); SaveAndRender(); };
            choices.Add(button, presets.Count);
            presets.Add(button);
        }
        var actions = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)], ColumnSpacing = 8 };
        toggle.Clicked += (_, _) =>
        {
            var now = DateTimeOffset.UtcNow;
            if (!rest.Started || rest.Remaining(now) <= 0) rest.Start(now);
            else if (rest.Deadline is not null) rest.Pause(now);
            else rest.Resume(now);
            SaveAndRender();
        };
        var extend = new Button { Text = "+15 s", Padding = 10, BackgroundColor = Color.FromArgb("#304465"), TextColor = Colors.White };
        extend.Clicked += (_, _) => { rest.Extend(DateTimeOffset.UtcNow); SaveAndRender(); };
        var reset = new Button { Text = "Zerar", Padding = 10, BackgroundColor = Colors.Transparent, TextColor = Colors.White };
        reset.Clicked += (_, _) => { rest.Reset(); SaveAndRender(); };
        actions.Add(toggle); actions.Add(extend, 1); actions.Add(reset, 2);
        Content = new VerticalStackLayout { Spacing = 10, Children = { header, progress, choices, actions } };
        ticker = Dispatcher.CreateTimer();
        ticker.Interval = TimeSpan.FromMilliseconds(250);
        ticker.Tick += (_, _) => Render();
        Render();
    }
    public void Activate() { Render(); ticker.Start(); }
    public void Deactivate() => ticker.Stop();
    public void StartRest() { rest.Start(DateTimeOffset.UtcNow); SaveAndRender(); }
    public void Clear() { ticker.Stop(); Preferences.Default.Remove(key); }
    private void SaveAndRender()
    {
        Preferences.Default.Set(key, JsonSerializer.Serialize(rest));
        Render();
    }
    private void Render()
    {
        var remaining = rest.Remaining(DateTimeOffset.UtcNow);
        var seconds = (int)Math.Ceiling(remaining);
        clock.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        status.Text = !rest.Started ? "Pronto para a próxima série" : remaining <= 0 ? "Concluído • próxima série!" : rest.Deadline is null ? "Pausado" : "Respire. Recupere sua energia.";
        toggle.Text = !rest.Started ? "Iniciar" : remaining <= 0 ? "Repetir" : rest.Deadline is null ? "Retomar" : "Pausar";
        progress.Progress = Math.Clamp(remaining / rest.DurationSeconds, 0, 1);
        for (var i = 0; i < presets.Count; i++)
        {
            var selected = rest.DurationSeconds == new[] { 60, 90, 120 }[i];
            presets[i].BackgroundColor = Color.FromArgb(selected ? "#66DAEB" : "#304465");
            presets[i].TextColor = selected ? Color.FromArgb("#102438") : Colors.White;
            presets[i].IsEnabled = !rest.Started || remaining <= 0;
        }
    }
}
