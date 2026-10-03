using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class GoalProgressPage : ContentPage
{
    private readonly GoalViewModel model;
    private readonly WorkoutViewModel workouts;
    private readonly VerticalStackLayout content = new() { Spacing = 20, Padding = new Thickness(24, 20, 24, 32) };
    private readonly Picker category = new() { Title = "Categoria", ItemsSource = new[] { "Visão geral", "Hábitos", "Corpo", "Metas", "Treino" }, SelectedIndex = 0 };
    private readonly Picker period = new() { Title = "Período", ItemsSource = new[] { "7 dias", "30 dias", "90 dias" }, SelectedIndex = 1 };
    private readonly int[] days = [7, 30, 90];
    public GoalProgressPage(GoalViewModel model, WorkoutViewModel workouts)
    {
        this.model = model;
        this.workouts = workouts;
        Title = "Progresso";
        category.SelectedIndexChanged += async (_, _) => await RenderAsync();
        period.SelectedIndexChanged += async (_, _) => await RenderAsync();
        Content = PageLayout.Body(PageLayout.Card(new Label { Text = "Categoria" }, category, new Label { Text = "Período" }, period), content);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await model.LoadAsync(); await workouts.LoadAsync(); await RenderAsync(); }
    private async Task RenderAsync()
    {
        content.Clear();
        content.Add(PageLayout.Muted("SEU PROGRESSO"));
        content.Add(PageLayout.Heading("Um passo de cada vez."));
        if (model.Error is { } error) { content.Add(PageLayout.Muted(error)); return; }

        var selection = category.SelectedIndex;
        var count = days[Math.Max(0, period.SelectedIndex)];
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (selection is 0 or 1) ShowHabits(today, count, selection == 1);
        if (selection is 0 or 2) ShowBody(today, count);
        if (selection is 0 or 3) await ShowGoalsAsync();
        if (selection is 0 or 4) ShowWorkout(today, count);
    }
    private void ShowHabits(DateOnly today, int count, bool details)
    {
        var report = ProgressAnalytics.Habits(model.Habits, today, count);
        var delta = (report.Current.Fraction - report.Previous.Fraction) * 100;
        content.Add(PageLayout.Card(new Label { Text = "Hábitos", FontSize = 21, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted(report.Current.Planned == 0 ? "Nenhuma oportunidade programada no período." :
                $"{report.Current.Done}/{report.Current.Planned} concluídos • {report.Current.Fraction:P0}"),
            PageLayout.Muted(report.Previous.Planned == 0 ? "Sem período anterior comparável." :
                $"Período anterior: {report.Previous.Fraction:P0} • mudança: {delta:+0.#;-0.#;0} pontos percentuais"),
            TrendChart.Create(report.WeeklyConsistency, "Sem hábitos programados para o gráfico.")));
        if (!details) return;
        content.Add(new Label { Text = "Calendário de consistência", FontSize = 20, FontAttributes = FontAttributes.Bold });
        content.Add(PageLayout.Muted("Verde: concluído  •  vermelho: faltou  •  amarelo: hoje pendente  •  cinza: descanso/pausa"));
        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        var cells = report.Heatmap.OrderBy(k => k.Key).ToList();
        for (var i = 0; i < cells.Count; i++)
        {
            var (date, state) = cells[i];
            var color = state switch
            {
                HabitDayState.Completed => "#427C57",
                HabitDayState.Missed => "#AF5555",
                HabitDayState.ScheduledPending => "#B39142",
                _ => "#57655D"
            };
            var cell = new Label { Text = date.Day.ToString(), HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center, HeightRequest = 36, BackgroundColor = Color.FromArgb(color),
                TextColor = Colors.White, FontSize = 12 };
            SemanticProperties.SetDescription(cell, $"{date:dd/MM/yyyy}: {DayLabel(state)}");
            grid.Add(cell, i % 7, i / 7);
        }
        content.Add(grid);
        content.Add(new Label { Text = "Por hábito", FontSize = 20, FontAttributes = FontAttributes.Bold });
        if (model.Habits.Count == 0) content.Add(PageLayout.Muted("Nenhum hábito criado ainda."));
        foreach (var habit in model.Habits.Where(h => h.IsActiveOn(today)))
        {
            var score = HabitAnalytics.Score(habit, today, count);
            var streak = HabitAnalytics.Build(habit, today).CurrentStreak;
            content.Add(PageLayout.Card(new Label { Text = habit.RevisionOn(today)?.Name ?? "Hábito", FontSize = 17, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted(score.Planned == 0 ? "Sem dias programados neste período" : $"{score.Done}/{score.Planned} concluídos • {score.Fraction:P0}"),
                PageLayout.Muted($"Sequência atual: {streak}")));
        }
    }
    private static string DayLabel(HabitDayState state) => state switch
    {
        HabitDayState.Completed => "concluído", HabitDayState.Missed => "não concluído",
        HabitDayState.ScheduledPending => "pendente", _ => "descanso ou pausa"
    };
    private void ShowBody(DateOnly today, int count)
    {
        var points = ProgressAnalytics.Weights(model.Weights, today, count);
        var measures = model.Measurements.Where(m => m.VoidedAt is null)
            .GroupBy(m => $"{m.Name} ({m.Unit})", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(m => m.MeasuredAt).First()).ToList();
        var views = new List<View> { new Label { Text = "Corpo", FontSize = 21, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted(points.Count == 0 ? "Sem pesagens neste período." : $"Último peso: {points[^1].Value:0.##} kg"),
            TrendChart.Create(points, "Registre uma pesagem para ver a evolução.") };
        foreach (var m in measures.Take(3)) views.Add(PageLayout.Muted($"{m.Name}: {GoalUi.Number(m.Value)} {m.Unit}"));
        var open = new Button { Text = "Abrir peso e medidas" };
        open.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new BodyPage(model)));
        views.Add(open);
        content.Add(PageLayout.Card(views.ToArray()));
    }
    private async Task ShowGoalsAsync()
    {
        var views = new List<View> { new Label { Text = "Metas", FontSize = 21, FontAttributes = FontAttributes.Bold } };
        var active = model.Goals.Where(g => !g.IsArchived).ToList();
        if (active.Count == 0) views.Add(PageLayout.Muted("Nenhuma meta ativa."));
        foreach (var goal in active.Take(5))
        {
            var current = await model.CurrentAsync(goal);
            views.Add(PageLayout.Muted(current is null ? $"{goal.Title} • sem registro atual" :
                $"{goal.Title} • {GoalUi.Number(current.Value)} / {GoalUi.Number(goal.TargetValue)} {goal.Unit}"));
            if (goal.Progress(current) is { } fraction) views.Add(new ProgressBar { Progress = fraction, ProgressColor = Color.FromArgb("#88E0B0") });
        }
        var goals = new Button { Text = "Abrir metas" };
        goals.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new GoalsPage(model)));
        var board = new Button { Text = "Abrir Plano de Ação" };
        board.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new ActionBoardPage(model)));
        views.Add(goals); views.Add(board);
        content.Add(PageLayout.Card(views.ToArray()));
    }
    private void ShowWorkout(DateOnly today, int count)
    {
        var report = WorkoutAnalytics.Frequency(workouts.Plans, workouts.Sessions, today, count);
        var open = new Button { Text = "Abrir evolução do treino" };
        open.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutProgressPage(workouts)));
        content.Add(PageLayout.Card(new Label { Text = "Treino", FontSize = 21, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted($"{report.Planned} programados • {report.Completed} realizados • {report.Missed} perdidos • {report.Rest} descansos"), open));
    }
}
