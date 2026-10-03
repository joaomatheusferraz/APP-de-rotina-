using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class WorkoutSessionPage : ContentPage
{
    private readonly WorkoutViewModel model;
    private readonly Guid sessionId;
    private readonly VerticalStackLayout content = new() { Spacing = 18, Padding = new Thickness(24, 20, 24, 32) };
    private readonly WorkoutRestPanel restPanel;
    public WorkoutSessionPage(WorkoutViewModel model, Guid sessionId)
    {
        this.model = model; this.sessionId = sessionId;
        Title = "Executar treino";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        restPanel = new WorkoutRestPanel(sessionId);
        var layout = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        layout.Add(new ScrollView { Content = content });
        layout.Add(restPanel, 0, 1);
        Content = layout;
    }
    protected override async void OnAppearing() { base.OnAppearing(); await model.LoadAsync(); Render(); if (restPanel.IsVisible) restPanel.Activate(); }
    protected override void OnDisappearing() { restPanel.Deactivate(); base.OnDisappearing(); }
    private void Render()
    {
        content.Clear();
        var session = model.Sessions.FirstOrDefault(s => s.Id == sessionId);
        restPanel.IsVisible = session is { EndedAt: null };
        if (session is null) { content.Add(PageLayout.Muted("Treino não encontrado.")); return; }
        var plan = model.PlanOf(session);
        var day = model.DayOf(session);
        if (plan is null || day is null) { content.Add(PageLayout.Muted("Plano original indisponível.")); return; }
        var phase = model.PhaseOf(session);
        content.Add(PageLayout.Heading(day.Name));
        content.Add(PageLayout.Muted($"{plan.Name} • versão {plan.Number} • {session.StartedAt.ToLocalTime():dd/MM HH:mm}"));
        if (phase is not null) content.Add(PageLayout.Muted($"Fase: {phase.Name} • {phase.TargetSets}×{phase.RepMin}–{phase.RepMax}"));
        var target = day.Exercises.Sum(e => phase?.TargetSets ?? e.TargetSets);
        content.Add(PageLayout.Card(
            new Label { Text = "SUA SESSÃO", FontSize = 11, CharacterSpacing = 2 },
            new Label { Text = $"{session.Sets.Count} / {target} séries", FontSize = 26, FontAttributes = FontAttributes.Bold },
            new ProgressBar { Progress = target == 0 ? 0 : Math.Clamp((double)session.Sets.Count / target, 0, 1),
                ProgressColor = Color.FromArgb("#38C9DE") },
            PageLayout.Muted($"Volume acumulado • {GoalUi.Number(session.VolumeKg)} kg")));
        if (session.EndedAt is not null)
        {
            content.Add(PageLayout.Muted($"Concluído • duração {WorkoutUi.Duration(session.Duration)}"));
            if (session.Note is { } note) content.Add(PageLayout.Muted(note));
        }
        foreach (var exercise in day.Exercises.OrderBy(e => e.Order))
        {
            var setsTarget = phase?.TargetSets ?? exercise.TargetSets;
            var min = phase?.RepMin ?? exercise.RepMin;
            var max = phase?.RepMax ?? exercise.RepMax;
            var previous = model.SetsForName(exercise.Name, plan.PlanId).LastOrDefault(s => s.CompletedAt < session.StartedAt);
            var views = new List<View> { new Label { Text = exercise.Name, FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"{exercise.MuscleGroup} • alvo {setsTarget} séries de {min}–{max} repetições"),
                PageLayout.Muted(previous is null ? "Sem execução anterior" : $"Última: {GoalUi.Number(previous.LoadKg)} kg × {previous.ActualReps} repetições") };
            if (!string.IsNullOrWhiteSpace(exercise.Note)) views.Add(PageLayout.Muted(exercise.Note));
            for (var number = 1; number <= setsTarget; number++)
            {
                var captured = number;
                var completed = session.Sets.FirstOrDefault(s => s.ExerciseId == exercise.Id && s.Number == captured);
                if (completed is not null)
                    views.Add(PageLayout.Muted($"✓ Série {captured}: {GoalUi.Number(completed.LoadKg)} kg × {completed.ActualReps} repetições"));
                else if (session.EndedAt is null)
                {
                    var add = PageLayout.Primary($"Concluir série {captured}");
                    add.Clicked += async (_, _) => await AddSetAsync(session, exercise, captured, min, max, previous);
                    views.Add(add);
                }
            }
            content.Add(PageLayout.Card(views.ToArray()));
        }
        if (session.EndedAt is null)
        {
            var finish = new Button { Text = "Finalizar treino" };
            finish.Clicked += async (_, _) =>
            {
                if (session.Sets.Count == 0 && !await DisplayAlert("Finalizar sem séries?", "Nenhuma série foi concluída.", "Finalizar", "Cancelar")) return;
                var note = await DisplayPromptAsync("Nota do treino", "Como foi a sessão? Deixe vazio se preferir.",
                    "Finalizar", "Cancelar", maxLength: 1000);
                if (note is null) return;
                try
                {
                    await model.FinishAsync(session, note);
                    restPanel.Clear();
                    await Navigation.PopModalAsync();
                }
                catch (Exception ex) { await DisplayAlert("Não foi possível finalizar", ex.Message, "Entendi"); }
            };
            content.Add(finish);
        }
    }
    private async Task AddSetAsync(WorkoutSession session, WorkoutExercise exercise, int number,
        int min, int max, WorkoutSet? previous)
    {
        var load = await DisplayPromptAsync("Carga da série", "Carga em kg (0 para peso corporal)", "Continuar", "Cancelar",
            keyboard: Keyboard.Numeric, initialValue: GoalUi.Number(previous?.LoadKg ?? exercise.SuggestedLoadKg ?? 0));
        if (load is null) return;
        var reps = await DisplayPromptAsync("Repetições realizadas", "Quantas repetições concluiu?", "Salvar", "Cancelar",
            keyboard: Keyboard.Numeric, initialValue: max.ToString());
        if (reps is null) return;
        try
        {
            if (!int.TryParse(reps, out var actual)) throw new ArgumentException("Informe repetições inteiras.");
            await model.AddSetAsync(session, exercise, number, min, max, GoalUi.Parse(load, "a carga"), actual);
            Render();
            restPanel.StartRest();
        }
        catch (Exception ex) { await DisplayAlert("Série inválida", ex.Message, "Entendi"); }
    }
}
