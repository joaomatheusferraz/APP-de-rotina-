using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class WorkoutHistoryPage : ContentPage
{
    private readonly WorkoutViewModel model;
    private readonly VerticalStackLayout content = new() { Spacing = 16, Padding = new Thickness(24, 20, 24, 32) };
    public WorkoutHistoryPage(WorkoutViewModel model)
    {
        this.model = model;
        Title = "Histórico de treinos";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        Content = new ScrollView { Content = content };
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await model.LoadAsync();
        content.Clear();
        content.Add(PageLayout.Heading("Histórico"));
        if (model.Sessions.Count == 0) { content.Add(PageLayout.Muted("Nenhum treino registrado ainda.")); return; }
        foreach (var session in model.Sessions.Reverse())
        {
            var plan = model.PlanOf(session);
            var day = model.DayOf(session);
            var phase = model.PhaseOf(session);
            var open = new Button { Text = "Ver séries" };
            open.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutSessionPage(model, session.Id)));
            content.Add(PageLayout.Card(new Label { Text = day?.Name ?? "Treino", FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"{session.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm} • {WorkoutUi.Duration(session.Duration)}"),
                PageLayout.Muted($"{plan?.Name ?? "Plano"} v{plan?.Number} • fase {phase?.Name ?? "livre"}"),
                PageLayout.Muted($"{session.Sets.Count} séries • volume {GoalUi.Number(session.VolumeKg)} kg"), open));
        }
    }
}

public sealed class WorkoutProgressPage : ContentPage
{
    private readonly WorkoutViewModel model;
    private readonly VerticalStackLayout content = new() { Spacing = 18, Padding = new Thickness(24, 20, 24, 32) };
    private readonly Picker period = new() { Title = "Período", ItemsSource = new[] { "7 dias", "30 dias", "90 dias" }, SelectedIndex = 1 };
    private readonly Picker exercise = new() { Title = "Exercício" };
    private readonly int[] days = [7, 30, 90];
    public WorkoutProgressPage(WorkoutViewModel model)
    {
        this.model = model;
        Title = "Evolução do treino";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        period.SelectedIndexChanged += (_, _) => Render();
        exercise.SelectedIndexChanged += (_, _) => Render();
        Content = PageLayout.Body(PageLayout.Card(new Label { Text = "Período" }, period, new Label { Text = "Exercício" }, exercise), content);
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await model.LoadAsync();
        var selected = exercise.SelectedItem?.ToString();
        var names = model.CurrentPlan?.Days.SelectMany(d => d.Exercises).Select(e => e.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList() ?? [];
        exercise.ItemsSource = names;
        if (names.Count > 0) exercise.SelectedItem = names.FirstOrDefault(n => n == selected) ?? names[0];
        Render();
    }
    private void Render()
    {
        content.Clear();
        content.Add(PageLayout.Heading("Evolução do treino"));
        var count = days[Math.Max(0, period.SelectedIndex)];
        var today = DateOnly.FromDateTime(DateTime.Today);
        var report = WorkoutAnalytics.Frequency(model.Plans, model.Sessions, today, count);
        content.Add(PageLayout.Card(
            new Label { Text = "Frequência", FontSize = 21, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted($"{report.Planned} programados • {report.Completed} realizados • {report.Missed} perdidos • {report.Rest} descansos")));
        content.Add(PageLayout.Muted("Verde: realizado • vermelho: perdido • amarelo: pendente • cinza: descanso • azul: treino extra"));
        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        foreach (var (item, i) in report.Days.OrderBy(k => k.Key).Select((value, index) => (value, index)))
        {
            var color = item.Value switch
            {
                WorkoutDayState.Completed => "#427C57", WorkoutDayState.Missed => "#AF5555",
                WorkoutDayState.Pending => "#B39142", WorkoutDayState.Extra => "#46779C", _ => "#57655D"
            };
            var cell = new Label { Text = item.Key.Day.ToString(), HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center, HeightRequest = 36, BackgroundColor = Color.FromArgb(color),
                TextColor = Colors.White, FontSize = 12 };
            SemanticProperties.SetDescription(cell, $"{item.Key:dd/MM/yyyy}: {item.Value}");
            grid.Add(cell, i % 7, i / 7);
        }
        content.Add(grid);
        var recent = model.Sessions.Where(s => s.EndedAt is not null &&
            DateOnly.FromDateTime(s.StartedAt.ToLocalTime().DateTime) >= today.AddDays(1 - count)).ToList();
        content.Add(PageLayout.Card(new Label { Text = "Volume do período", FontSize = 20, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted($"{GoalUi.Number(recent.Sum(s => s.VolumeKg))} kg • {recent.Count} sessões")));
        var plan = model.CurrentPlan;
        if (plan is null || exercise.Items.Count == 0) return;

        var name = exercise.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(name)) return;
        var sets = model.SetsForName(name, plan.PlanId);
        var inPeriod = sets.Where(s => DateOnly.FromDateTime(s.CompletedAt.ToLocalTime().DateTime) >= today.AddDays(1 - count)).ToList();
        var points = inPeriod.Select(s => new TrendPoint(DateOnly.FromDateTime(s.CompletedAt.ToLocalTime().DateTime), (double)s.LoadKg)).ToList();
        content.Add(PageLayout.Card(new Label { Text = name, FontSize = 20, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted(sets.Count == 0 ? "Sem execução anterior" :
                $"Recordes: {GoalUi.Number(sets.Max(s => s.LoadKg))} kg • {sets.Max(s => s.ActualReps)} repetições"),
            PageLayout.Muted($"Volume acumulado: {GoalUi.Number(sets.Sum(s => s.VolumeKg))} kg"),
            TrendChart.Create(points, "Registre séries para ver a evolução de carga.")));
    }
}
