using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class WorkoutCyclePage : ContentPage
{
    private readonly WorkoutViewModel model;
    private readonly VerticalStackLayout content = new() { Spacing = 18, Padding = new Thickness(24, 20, 24, 32) };
    public WorkoutCyclePage(WorkoutViewModel model)
    {
        this.model = model;
        Title = "Periodização";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        Content = new ScrollView { Content = content };
    }
    protected override async void OnAppearing() { base.OnAppearing(); await RefreshAsync(); }
    private async Task RefreshAsync() { await model.LoadAsync(); Render(); }
    private void Render()
    {
        content.Clear();
        content.Add(PageLayout.Heading("Periodização"));
        content.Add(PageLayout.Muted("Defina fases, semanas e faixas de séries/repetições. Uma troca preserva os ciclos e treinos anteriores."));
        var cycle = model.CurrentCycle;
        if (cycle is not null)
        {
            var phase = model.CurrentPhase;
            var views = new List<View> { new Label { Text = cycle.Name, FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"Iniciado em {cycle.StartedOn:dd/MM/yyyy}"),
                PageLayout.Muted(cycle.PausedOn is null ? $"Fase atual: {phase?.Name ?? "—"}" : "Pausado") };
            foreach (var p in cycle.Phases.OrderBy(p => p.Order))
                views.Add(PageLayout.Muted($"{p.Name}: {p.Weeks} semana(s), {p.TargetSets} séries, {p.RepMin}–{p.RepMax} repetições"));
            var pause = new Button { Text = cycle.PausedOn is null ? "Pausar ciclo" : "Retomar ciclo" };
            pause.Clicked += async (_, _) =>
            {
                try
                {
                    if (cycle.PausedOn is null) await model.PauseCycleAsync(cycle.Id);
                    else await model.ResumeCycleAsync(cycle.Id);
                    Render();
                }
                catch (Exception ex) { await DisplayAlert("Não foi possível alterar", ex.Message, "Entendi"); }
            };
            views.Add(pause);
            content.Add(PageLayout.Card(views.ToArray()));
        }
        var create = new Button { Text = cycle is null ? "+ Criar ciclo" : "Trocar ciclo ou editar fases" };
        create.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutCycleEditorPage(model, cycle, RefreshAsync)));
        content.Add(create);
        if (cycle is not null)
        {
            var restart = new Button { Text = "Reiniciar ciclo desde a primeira fase" };
            restart.Clicked += async (_, _) =>
            {
                if (!await DisplayAlert("Reiniciar ciclo?", "O ciclo atual ficará no histórico e um novo começará hoje.", "Reiniciar", "Cancelar")) return;
                var today = DateOnly.FromDateTime(DateTime.Today);
                var copy = new WorkoutCycle(Guid.NewGuid(), cycle.Name, today, null, 0, null,
                    cycle.Phases.Select(p => p with { Id = Guid.NewGuid() }).ToList());
                try { await model.SaveCycleAsync(copy); Render(); }
                catch (Exception ex) { await DisplayAlert("Não foi possível reiniciar", ex.Message, "Entendi"); }
            };
            content.Add(restart);
        }
        content.Add(new Label { Text = "Ciclos anteriores", FontSize = 20, FontAttributes = FontAttributes.Bold });
        foreach (var old in model.Cycles.Where(c => c.ArchivedOn is not null).Reverse())
            content.Add(PageLayout.Card(new Label { Text = old.Name, FontSize = 18, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"{old.StartedOn:dd/MM/yyyy} – {old.ArchivedOn:dd/MM/yyyy}"),
                PageLayout.Muted(string.Join(" → ", old.Phases.OrderBy(p => p.Order).Select(p => p.Name)))));
    }
}

public sealed class WorkoutCycleEditorPage : ContentPage
{
    private readonly WorkoutViewModel model;
    private readonly Func<Task> saved;
    private readonly Entry name = new() { Placeholder = "Nome do ciclo", MaxLength = 120 };
    private readonly List<WorkoutPhase> phases = [];
    private readonly VerticalStackLayout list = new() { Spacing = 12 };
    private readonly Label error = PageLayout.Muted("");
    public WorkoutCycleEditorPage(WorkoutViewModel model, WorkoutCycle? existing, Func<Task> saved)
    {
        this.model = model; this.saved = saved;
        Title = existing is null ? "Novo ciclo" : "Novo ciclo de fases";
        name.Text = existing?.Name ?? "";
        if (existing is not null) phases.AddRange(existing.Phases.Select(p => p with { Id = Guid.NewGuid() }));
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        var template = new Button { Text = "Usar sugestão 2 + 2 + 1 semanas" };
        template.Clicked += async (_, _) =>
        {
            if (phases.Count > 0 && !await DisplayAlert("Substituir fases?", "O rascunho atual será trocado pela sugestão editável.", "Substituir", "Cancelar")) return;
            phases.Clear();
            phases.AddRange([
                new WorkoutPhase(Guid.NewGuid(), "Hipertrofia", 0, 2, 3, 8, 12, null),
                new WorkoutPhase(Guid.NewGuid(), "Força", 1, 2, 4, 3, 5, null),
                new WorkoutPhase(Guid.NewGuid(), "Deload", 2, 1, 2, 10, 15, null)]);
            if (string.IsNullOrWhiteSpace(name.Text)) name.Text = "Ciclo de treino";
            Render();
        };
        var add = new Button { Text = "+ Adicionar fase" };
        add.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutPhaseEditorPage(null, updated =>
        { phases.Add(updated with { Order = phases.Count }); Render(); return Task.CompletedTask; })));
        var save = new Button { Text = "Iniciar novo ciclo" };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var cycle = new WorkoutCycle(Guid.NewGuid(), name.Text ?? "", DateOnly.FromDateTime(DateTime.Today),
                    null, 0, null, phases.Select((p, i) => p with { Order = i }).ToList());
                await model.SaveCycleAsync(cycle);
                await Navigation.PopModalAsync();
                await saved();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading(Title),
            PageLayout.Muted("A sugestão do PRD é opcional e pode ser totalmente alterada."),
            PageLayout.Card(new Label { Text = "Nome" }, name), template, add, list, error, save);
        Render();
    }
    private void Render()
    {
        list.Clear();
        if (phases.Count == 0) { list.Add(PageLayout.Muted("Adicione uma ou mais fases.")); return; }
        foreach (var phase in phases.ToList())
        {
            var edit = new Button { Text = "Editar fase" };
            edit.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutPhaseEditorPage(phase, updated =>
            { phases[phases.FindIndex(p => p.Id == phase.Id)] = updated; Render(); return Task.CompletedTask; })));
            var remove = new Button { Text = "Remover fase" };
            remove.Clicked += async (_, _) => { if (await DisplayAlert("Remover fase?", phase.Name, "Remover", "Cancelar")) { phases.Remove(phase); Render(); } };
            list.Add(PageLayout.Card(new Label { Text = phase.Name, FontSize = 18, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"{phase.Weeks} semana(s) • {phase.TargetSets}×{phase.RepMin}–{phase.RepMax}"),
                PageLayout.Muted(phase.Note ?? ""), edit, remove));
        }
    }
}

public sealed class WorkoutPhaseEditorPage : ContentPage
{
    public WorkoutPhaseEditorPage(WorkoutPhase? existing, Func<WorkoutPhase, Task> saved)
    {
        Title = existing is null ? "Nova fase" : "Editar fase";
        var name = new Entry { Placeholder = "Ex.: Hipertrofia", Text = existing?.Name, MaxLength = 80 };
        var weeks = new Entry { Placeholder = "Semanas", Text = (existing?.Weeks ?? 2).ToString(), Keyboard = Keyboard.Numeric };
        var sets = new Entry { Placeholder = "Séries", Text = (existing?.TargetSets ?? 3).ToString(), Keyboard = Keyboard.Numeric };
        var min = new Entry { Placeholder = "Mínimo", Text = (existing?.RepMin ?? 8).ToString(), Keyboard = Keyboard.Numeric };
        var max = new Entry { Placeholder = "Máximo", Text = (existing?.RepMax ?? 12).ToString(), Keyboard = Keyboard.Numeric };
        var note = new Editor { Placeholder = "Observações opcionais", Text = existing?.Note, HeightRequest = 90, MaxLength = 1000 };
        var error = PageLayout.Muted("");
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        var save = new Button { Text = "Salvar fase" };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                if (!int.TryParse(weeks.Text, out var weekCount) || !int.TryParse(sets.Text, out var setCount) ||
                    !int.TryParse(min.Text, out var repMin) || !int.TryParse(max.Text, out var repMax))
                    throw new ArgumentException("Informe números inteiros válidos.");
                var phase = new WorkoutPhase(existing?.Id ?? Guid.NewGuid(), name.Text?.Trim() ?? "", existing?.Order ?? 0,
                    weekCount, setCount, repMin, repMax, note.Text);
                phase.Validate();
                await saved(phase);
                await Navigation.PopModalAsync();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading(Title), PageLayout.Card(new Label { Text = "Nome" }, name,
            new Label { Text = "Semanas" }, weeks, new Label { Text = "Séries alvo" }, sets,
            new Label { Text = "Repetições mínimas" }, min, new Label { Text = "Repetições máximas" }, max,
            new Label { Text = "Observações" }, note, error, save));
    }
}
