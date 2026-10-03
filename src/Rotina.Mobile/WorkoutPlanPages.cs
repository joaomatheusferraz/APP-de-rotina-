using Rotina.Domain;

namespace Rotina.Mobile;

internal static class WorkoutUi
{
    public static readonly string[] Weekdays = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];
    public static string Day(DayOfWeek day) => Weekdays[(int)day];
    public static string Duration(TimeSpan? duration) => duration is null ? "Em andamento" :
        $"{(int)duration.Value.TotalMinutes} min";
}

public sealed class WorkoutPage : ContentPage
{
    private readonly WorkoutViewModel model;
    private readonly VerticalStackLayout content = new() { Spacing = 20, Padding = new Thickness(24, 20, 24, 32) };
    public WorkoutPage(WorkoutViewModel model)
    {
        this.model = model;
        Title = "Treino";
        Content = new ScrollView { Content = content };
    }
    protected override async void OnAppearing() { base.OnAppearing(); await RefreshAsync(); }
    private async Task RefreshAsync() { await model.LoadAsync(); Render(); }
    private void Render()
    {
        content.Clear();
        content.Add(PageLayout.Hero("MOVIMENTO • CONSTÂNCIA", "Supere seus limites.", "Seu plano, suas séries e seu tempo de recuperação."));
        if (model.Error is { } error) { content.Add(PageLayout.Muted(error)); return; }
        var current = model.CurrentPlan;
        var open = model.OpenSession;
        if (open is not null)
        {
            var resume = PageLayout.Primary("Retomar treino em andamento");
            resume.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutSessionPage(model, open.Id)));
            content.Add(PageLayout.Card(new Label { Text = "Treino em andamento", FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"Iniciado em {open.StartedAt.ToLocalTime():dd/MM HH:mm}"), resume));
        }
        var plan = new Button { Text = current is null ? "+ Criar plano de treino" : "Criar nova versão do plano" };
        plan.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutPlanEditorPage(model, current, RefreshAsync)));
        var versions = new Button { Text = "Planos anteriores" };
        versions.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutVersionsPage(model)));
        var history = new Button { Text = "Histórico e recordes" };
        history.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutHistoryPage(model)));
        var progress = new Button { Text = "Frequência e evolução" };
        progress.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutProgressPage(model)));
        var cycle = new Button { Text = model.CurrentCycle is null ? "Configurar periodização" : "Ver ou alterar periodização" };
        cycle.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutCyclePage(model)));
        content.Add(PageLayout.Card(new Label { Text = "Plano atual", FontSize = 21, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted(current is null ? "Nenhum plano criado. Comece escolhendo seus dias e exercícios." :
                $"{current.Name} • versão {current.Number}"), plan, versions, cycle, history, progress));
        if (model.CurrentCycle is { } activeCycle)
            content.Add(PageLayout.Card(new Label { Text = "Fase atual", FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted(activeCycle.PausedOn is null ? model.CurrentPhase?.Name ?? "Sem fase ativa" : "Ciclo pausado"),
                PageLayout.Muted(model.CurrentPhase is { } phase ? $"{phase.TargetSets} séries • {phase.RepMin}–{phase.RepMax} repetições" : "")));
        if (current is null) return;
        content.Add(new Label { Text = "Semana", FontSize = 22, FontAttributes = FontAttributes.Bold });
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var weekday in Enumerable.Range(0, 7).Select(i => (DayOfWeek)((i + 1) % 7)))
        {
            var day = current.Days.FirstOrDefault(d => d.Weekday == weekday);
            if (day is null)
            {
                content.Add(PageLayout.Card(new Label { Text = WorkoutUi.Day(weekday), FontSize = 18, FontAttributes = FontAttributes.Bold }, PageLayout.Muted("Descanso")));
                continue;
            }
            var start = PageLayout.Primary(open is null ? "Iniciar treino" : "Há um treino em andamento");
            start.IsEnabled = open is null;
            start.Clicked += async (_, _) =>
            {
                try
                {
                    await model.StartAsync(current, day);
                    await Navigation.PushModalAsync(new NavigationPage(new WorkoutSessionPage(model, model.OpenSession!.Id)));
                    Render();
                }
                catch (Exception ex) { await DisplayAlert("Não foi possível iniciar", ex.Message, "Entendi"); }
            };
            content.Add(PageLayout.Card(new Label { Text = WorkoutUi.Day(weekday) + (weekday == today.DayOfWeek ? " • hoje" : ""),
                    FontSize = 18, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"{day.Name} • {day.Exercises.Count} exercício(s)"), start));
        }
    }
}

internal sealed class WorkoutDayDraft
{
    public DayOfWeek Weekday { get; set; }
    public string Name { get; set; } = "Treino";
    public List<WorkoutExercise> Exercises { get; } = [];
}

public sealed class WorkoutPlanEditorPage : ContentPage
{
    private readonly WorkoutViewModel model;
    private readonly WorkoutPlanVersion? existing;
    private readonly Func<Task> saved;
    private readonly Entry name = new() { Placeholder = "Nome do plano", MaxLength = 120 };
    private readonly VerticalStackLayout list = new() { Spacing = 14 };
    private readonly List<WorkoutDayDraft> days = [];
    private readonly Label error = PageLayout.Muted("");

    public WorkoutPlanEditorPage(WorkoutViewModel model, WorkoutPlanVersion? existing, Func<Task> saved)
    {
        this.model = model; this.existing = existing; this.saved = saved;
        Title = existing is null ? "Novo plano" : "Nova versão";
        name.Text = existing?.Name ?? "";
        if (existing is not null)
            foreach (var day in existing.Days)
            {
                var draft = new WorkoutDayDraft { Weekday = day.Weekday, Name = day.Name };
                draft.Exercises.AddRange(day.Exercises.Select(e => e with { Id = Guid.NewGuid() }));
                days.Add(draft);
            }
        var close = new ToolbarItem { Text = "Cancelar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        var add = new Button { Text = "+ Dia de treino" };
        add.Clicked += async (_, _) =>
        {
            var available = Enumerable.Range(0, 7).Select(i => (DayOfWeek)i).Where(d => days.All(x => x.Weekday != d)).ToList();
            if (available.Count == 0) { await DisplayAlert("Semana completa", "Todos os dias já foram adicionados.", "Entendi"); return; }
            var picked = await DisplayActionSheet("Escolha o dia", "Cancelar", null, available.Select(WorkoutUi.Day).ToArray());
            var weekday = available.FirstOrDefault(d => WorkoutUi.Day(d) == picked);
            if (picked is null || picked == "Cancelar") return;
            days.Add(new WorkoutDayDraft { Weekday = weekday, Name = $"Treino {WorkoutUi.Day(weekday)}" });
            Render();
        };
        var save = new Button { Text = existing is null ? "Salvar plano" : "Criar nova versão" };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var version = new WorkoutPlanVersion(Guid.NewGuid(), existing?.PlanId ?? Guid.NewGuid(),
                    (existing?.Number ?? 0) + 1, name.Text ?? "", today, null,
                    days.Select((d, i) => new WorkoutDay(Guid.NewGuid(), d.Weekday, d.Name, i,
                        d.Exercises.Select((e, j) => e with { Order = j }).ToList())).ToList());
                await model.SavePlanAsync(version);
                await Navigation.PopModalAsync();
                await saved();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading(Title),
            PageLayout.Muted("Escolha os dias e exercícios. Salvar uma nova versão preserva as sessões do plano anterior."),
            PageLayout.Card(new Label { Text = "Nome do plano" }, name), add, list, error, save);
        Render();
    }
    private void Render()
    {
        list.Clear();
        if (days.Count == 0) { list.Add(PageLayout.Muted("Adicione ao menos um dia de treino.")); return; }
        foreach (var day in days.OrderBy(d => ((int)d.Weekday + 6) % 7))
        {
            var views = new List<View> { new Label { Text = WorkoutUi.Day(day.Weekday), FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted(day.Name) };
            var rename = new Button { Text = "Renomear dia" };
            rename.Clicked += async (_, _) =>
            {
                var answer = await DisplayPromptAsync("Nome do treino", "Ex.: Superior A", "Salvar", "Cancelar", maxLength: 100, initialValue: day.Name);
                if (!string.IsNullOrWhiteSpace(answer)) { day.Name = answer.Trim(); Render(); }
            };
            views.Add(rename);
            foreach (var exercise in day.Exercises.OrderBy(e => e.Order).ToList())
            {
                views.Add(PageLayout.Muted($"{exercise.Name} • {exercise.TargetSets}×{exercise.RepMin}–{exercise.RepMax}"));
                var edit = new Button { Text = $"Editar {exercise.Name}" };
                edit.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutExerciseEditorPage(exercise, updated =>
                {
                    day.Exercises[day.Exercises.FindIndex(e => e.Id == exercise.Id)] = updated;
                    Render(); return Task.CompletedTask;
                })));
                views.Add(edit);
                var remove = new Button { Text = $"Remover {exercise.Name}" };
                remove.Clicked += async (_, _) =>
                {
                    if (await DisplayAlert("Remover exercício?", exercise.Name, "Remover", "Cancelar"))
                    { day.Exercises.Remove(exercise); Render(); }
                };
                views.Add(remove);
            }
            var addExercise = new Button { Text = "+ Exercício" };
            addExercise.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutExerciseEditorPage(null, updated =>
            { day.Exercises.Add(updated with { Order = day.Exercises.Count }); Render(); return Task.CompletedTask; })));
            views.Add(addExercise);
            var removeDay = new Button { Text = "Remover dia" };
            removeDay.Clicked += async (_, _) =>
            {
                if (await DisplayAlert("Remover dia?", "Os exercícios deste rascunho sairão da nova versão. O plano antigo permanece salvo.", "Remover", "Cancelar"))
                { days.Remove(day); Render(); }
            };
            views.Add(removeDay);
            list.Add(PageLayout.Card(views.ToArray()));
        }
    }
}

public sealed class WorkoutExerciseEditorPage : ContentPage
{
    public WorkoutExerciseEditorPage(WorkoutExercise? existing, Func<WorkoutExercise, Task> saved)
    {
        Title = existing is null ? "Novo exercício" : "Editar exercício";
        var name = new Entry { Placeholder = "Ex.: Agachamento", Text = existing?.Name, MaxLength = 100 };
        var muscle = new Entry { Placeholder = "Ex.: Pernas", Text = existing?.MuscleGroup, MaxLength = 80 };
        var sets = new Entry { Placeholder = "3", Text = (existing?.TargetSets ?? 3).ToString(), Keyboard = Keyboard.Numeric };
        var min = new Entry { Placeholder = "8", Text = (existing?.RepMin ?? 8).ToString(), Keyboard = Keyboard.Numeric };
        var max = new Entry { Placeholder = "12", Text = (existing?.RepMax ?? 12).ToString(), Keyboard = Keyboard.Numeric };
        var load = new Entry { Placeholder = "Opcional", Text = existing?.SuggestedLoadKg is { } kg ? GoalUi.Number(kg) : "", Keyboard = Keyboard.Numeric };
        var note = new Editor { Placeholder = "Observações opcionais", Text = existing?.Note, HeightRequest = 90, MaxLength = 1000 };
        var error = PageLayout.Muted("");
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        var save = new Button { Text = "Salvar exercício" };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                if (!int.TryParse(sets.Text, out var setCount) || !int.TryParse(min.Text, out var repMin) || !int.TryParse(max.Text, out var repMax))
                    throw new ArgumentException("Informe séries e repetições inteiras.");
                var exercise = new WorkoutExercise(existing?.Id ?? Guid.NewGuid(), name.Text?.Trim() ?? "", muscle.Text?.Trim() ?? "",
                    note.Text, existing?.Order ?? 0, setCount, repMin, repMax,
                    string.IsNullOrWhiteSpace(load.Text) ? null : GoalUi.Parse(load.Text, "a carga"));
                exercise.Validate();
                await saved(exercise);
                await Navigation.PopModalAsync();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading(Title), PageLayout.Card(new Label { Text = "Nome" }, name,
            new Label { Text = "Grupo muscular" }, muscle, new Label { Text = "Séries alvo" }, sets,
            new Label { Text = "Repetições mínimas" }, min, new Label { Text = "Repetições máximas" }, max,
            new Label { Text = "Carga sugerida (kg)" }, load, new Label { Text = "Observações" }, note, error, save));
    }
}

public sealed class WorkoutVersionsPage : ContentPage
{
    public WorkoutVersionsPage(WorkoutViewModel model)
    {
        Title = "Versões do plano";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        var rows = new List<View> { PageLayout.Heading("Planos preservados") };
        foreach (var plan in model.Plans.Reverse())
        {
            var lines = new List<View> { new Label { Text = $"{plan.Name} • versão {plan.Number}", FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"Válido desde {plan.EffectiveFrom:dd/MM/yyyy}" + (plan.SupersededOn is { } end ? $" até {end:dd/MM/yyyy}" : " • atual")) };
            foreach (var day in plan.Days.OrderBy(d => d.Weekday))
                lines.Add(PageLayout.Muted($"{WorkoutUi.Day(day.Weekday)}: {day.Name} — {string.Join(", ", day.Exercises.Select(e => e.Name))}"));
            rows.Add(PageLayout.Card(lines.ToArray()));
        }
        if (model.Plans.Count == 0) rows.Add(PageLayout.Muted("Nenhum plano ainda."));
        Content = PageLayout.Body(rows.ToArray());
    }
}
