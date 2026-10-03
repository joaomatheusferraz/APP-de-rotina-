using System.Globalization;
using Rotina.Domain;

namespace Rotina.Mobile;

internal static class GoalUi
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");
    public static string Number(decimal value) => value.ToString("0.##", Culture);
    public static decimal Parse(string? value, string label)
    {
        if (!decimal.TryParse(value?.Trim().Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result))
            throw new ArgumentException($"Informe {label} com um número válido.");
        return result;
    }
}

public sealed class GoalsPage : ContentPage
{
    private readonly GoalViewModel model;
    private readonly VerticalStackLayout list = new() { Spacing = 16 };
    private readonly Switch showArchived = new();

    public GoalsPage(GoalViewModel model)
    {
        this.model = model;
        Title = "Metas";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        var add = new Button { Text = "+ Nova meta" };
        add.Clicked += async (_, _) => await EditAsync(null, false);
        var project = new Button { Text = "Criar Projeto 80 kg" };
        project.Clicked += async (_, _) => await EditAsync(null, true);
        var board = new Button { Text = "Abrir Plano de Ação" };
        board.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new ActionBoardPage(model)));
        showArchived.Toggled += async (_, _) => await RenderAsync();
        Content = PageLayout.Body(PageLayout.Heading("Metas"),
            PageLayout.Muted("Acompanhe o progresso sem perder os registros antigos."),
            add, project, board,
            new HorizontalStackLayout { Spacing = 10, Children = { showArchived,
                new Label { Text = "Mostrar concluídas e arquivadas", VerticalOptions = LayoutOptions.Center } } }, list);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await RefreshAsync(); }
    private async Task RefreshAsync() { await model.LoadAsync(); await RenderAsync(); }
    private async Task RenderAsync()
    {
        list.Clear();
        if (model.Error is { } error) { list.Add(PageLayout.Muted(error)); return; }
        var goals = model.Goals.Where(g => showArchived.IsToggled || !g.IsArchived).ToList();
        if (goals.Count == 0) { list.Add(PageLayout.Muted("Nenhuma meta aqui. Crie uma meta ou revise o modelo Projeto 80 kg.")); return; }
        foreach (var goal in goals)
        {
            var current = await model.CurrentAsync(goal);
            var fraction = goal.Progress(current);
            var text = current is null ? "Progresso ainda não registrado" :
                fraction is null ? $"{GoalUi.Number(current.Value)} {goal.Unit} • informe o valor inicial para calcular o progresso" :
                $"{GoalUi.Number(current.Value)} de {GoalUi.Number(goal.TargetValue)} {goal.Unit} • {fraction:P0}";
            var open = new Button { Text = "Ver meta e histórico" };
            open.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new GoalDetailPage(model, goal.Id)));
            var edit = new Button { Text = "Editar meta" };
            edit.Clicked += async (_, _) => await EditAsync(goal, false);
            var complete = new Button { Text = goal.CompletedAt is null ? "Concluir e arquivar" : "Reabrir meta" };
            complete.Clicked += async (_, _) =>
            {
                if (goal.CompletedAt is null && !await DisplayAlert("Concluir meta?", "Ela ficará arquivada com o histórico preservado.", "Concluir", "Cancelar")) return;
                await model.CompleteGoalAsync(goal, goal.CompletedAt is null);
                await RenderAsync();
            };
            var archive = new Button { Text = goal.IsArchived ? "Restaurar" : "Arquivar" };
            archive.IsVisible = goal.CompletedAt is null;
            archive.Clicked += async (_, _) =>
            {
                if (!goal.IsArchived && !await DisplayAlert("Arquivar meta?", "O histórico permanecerá disponível.", "Arquivar", "Cancelar")) return;
                await model.ArchiveGoalAsync(goal, !goal.IsArchived);
                await RenderAsync();
            };
            list.Add(PageLayout.Card(new Label { Text = goal.Title, FontSize = 21, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted(goal.Kind == GoalKind.BodyWeight ? "Meta de peso" : "Meta numérica"),
                PageLayout.Muted(text), new ProgressBar { Progress = fraction ?? 0, ProgressColor = Color.FromArgb("#88E0B0") },
                PageLayout.Muted(goal.CompletedAt is null ? (goal.DueOn is { } due ? $"Prazo: {due:dd/MM/yyyy}" : "Sem prazo") : "Concluída e preservada"),
                open, edit, complete, archive));
        }
    }
    private async Task EditAsync(Goal? goal, bool template) =>
        await Navigation.PushModalAsync(new NavigationPage(new GoalEditorPage(model, goal, template, RefreshAsync)));
}

public sealed class GoalEditorPage : ContentPage
{
    private readonly GoalViewModel model;
    private readonly Goal? existing;
    private readonly Func<Task> saved;
    private readonly Entry title = new() { Placeholder = "Nome da meta", MaxLength = 120 };
    private readonly Editor description = new() { Placeholder = "Descrição opcional", HeightRequest = 100, MaxLength = 2000 };
    private readonly Picker kind = new() { ItemsSource = new[] { "Progresso numérico", "Peso corporal" } };
    private readonly DatePicker start = new() { Format = "dd/MM/yyyy" };
    private readonly Switch hasDue = new();
    private readonly DatePicker due = new() { Format = "dd/MM/yyyy" };
    private readonly Entry unit = new() { Placeholder = "Ex.: livros, horas, km", MaxLength = 30 };
    private readonly Entry baseline = new() { Placeholder = "Opcional", Keyboard = Keyboard.Numeric };
    private readonly Entry target = new() { Placeholder = "Valor-alvo", Keyboard = Keyboard.Numeric };
    private readonly Picker area;
    private readonly Label error = PageLayout.Muted("");

    public GoalEditorPage(GoalViewModel model, Goal? existing, bool template, Func<Task> saved)
    {
        this.model = model; this.existing = existing; this.saved = saved;
        Title = existing is null ? "Nova meta" : "Editar meta";
        title.Text = existing?.Title ?? (template ? "Projeto 80 kg" : "");
        description.Text = existing?.Description ?? "";
        kind.SelectedIndex = (int)(existing?.Kind ?? (template ? GoalKind.BodyWeight : GoalKind.Numeric));
        kind.IsEnabled = existing is null;
        start.Date = existing?.StartsOn.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        hasDue.IsToggled = existing?.DueOn is not null;
        due.Date = existing?.DueOn?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today.AddMonths(3);
        due.IsVisible = hasDue.IsToggled;
        hasDue.Toggled += (_, _) => due.IsVisible = hasDue.IsToggled;
        unit.Text = existing?.Unit ?? (template ? "kg" : "");
        baseline.Text = existing?.StartValue is { } initial ? GoalUi.Number(initial) : (template ? "62" : "0");
        target.Text = existing is not null ? GoalUi.Number(existing.TargetValue) : (template ? "80" : "");
        var areas = model.Areas.Where(a => !a.IsArchived || a.Id == existing?.AreaId).ToList();
        area = PlanningUi.AreaPicker(areas, existing?.AreaId);
        kind.SelectedIndexChanged += (_, _) =>
        {
            if (kind.SelectedIndex == (int)GoalKind.BodyWeight) unit.Text = "kg";
            unit.IsEnabled = kind.SelectedIndex != (int)GoalKind.BodyWeight;
        };
        unit.IsEnabled = kind.SelectedIndex != (int)GoalKind.BodyWeight;
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        var save = new Button { Text = "Salvar meta" };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var currentAreas = model.Areas.Where(a => !a.IsArchived || a.Id == existing?.AreaId).ToList();
                var initial = string.IsNullOrWhiteSpace(baseline.Text) ? (decimal?)null : GoalUi.Parse(baseline.Text, "o valor inicial");
                var goal = new Goal(existing?.Id ?? Guid.NewGuid(), title.Text ?? "", description.Text,
                    DateOnly.FromDateTime(start.Date), hasDue.IsToggled ? DateOnly.FromDateTime(due.Date) : null,
                    unit.Text ?? "", initial, GoalUi.Parse(target.Text, "o valor-alvo"),
                    PlanningUi.SelectedArea(area, currentAreas), (GoalKind)kind.SelectedIndex,
                    existing?.CompletedAt, existing?.IsArchived ?? false);
                await model.SaveGoalAsync(goal);
                await Navigation.PopModalAsync();
                await saved();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading(Title),
            PageLayout.Muted(template ? "Modelo sugerido pelo PRD. Confirme ou altere os 62 kg iniciais e o alvo de 80 kg antes de salvar; nenhum peso será registrado automaticamente." :
                "O progresso é registrado separadamente. Editar a meta preserva o histórico."),
            PageLayout.Card(new Label { Text = "Título" }, title, new Label { Text = "Descrição" }, description,
                new Label { Text = "Tipo" }, kind, new Label { Text = "Data inicial" }, start,
                new HorizontalStackLayout { Spacing = 10, Children = { hasDue, new Label { Text = "Definir prazo", VerticalOptions = LayoutOptions.Center } } }, due,
                new Label { Text = "Unidade" }, unit, new Label { Text = "Valor inicial" }, baseline,
                new Label { Text = "Valor-alvo" }, target, new Label { Text = "Área" }, area, error, save));
    }
}

public sealed class GoalDetailPage : ContentPage
{
    private readonly GoalViewModel model;
    private readonly Guid goalId;
    private readonly VerticalStackLayout content = new() { Spacing = 20 };
    public GoalDetailPage(GoalViewModel model, Guid goalId)
    {
        this.model = model; this.goalId = goalId;
        Title = "Detalhes da meta";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        Content = new ScrollView { Content = content };
        content.Padding = new Thickness(24, 20, 24, 32);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await RefreshAsync(); }
    private async Task RefreshAsync() { await model.LoadAsync(); await RenderAsync(); }
    private async Task RenderAsync()
    {
        content.Clear();
        var goal = model.Goals.FirstOrDefault(g => g.Id == goalId);
        if (goal is null) { content.Add(PageLayout.Muted("Meta não encontrada.")); return; }
        content.Add(PageLayout.Heading(goal.Title));
        if (goal.Description is { } description) content.Add(PageLayout.Muted(description));
        var current = await model.CurrentAsync(goal);
        var fraction = goal.Progress(current);
        content.Add(PageLayout.Card(
            new Label { Text = "Progresso", FontSize = 20, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted($"Inicial: {(goal.StartValue is { } initial ? GoalUi.Number(initial) : "não informado")} {goal.Unit}"),
            PageLayout.Muted($"Atual: {(current is { } value ? GoalUi.Number(value) : "não registrado")} {goal.Unit}"),
            PageLayout.Muted($"Alvo: {GoalUi.Number(goal.TargetValue)} {goal.Unit}"),
            new ProgressBar { Progress = fraction ?? 0, ProgressColor = Color.FromArgb("#88E0B0") }));
        var add = new Button { Text = goal.Kind == GoalKind.BodyWeight ? "Registrar pesagem" : "Registrar progresso" };
        add.Clicked += async (_, _) =>
        {
            if (goal.Kind == GoalKind.BodyWeight)
                await Navigation.PushModalAsync(new NavigationPage(new WeightEditorPage(model, RefreshAsync)));
            else await AddProgressAsync(goal);
        };
        var link = new Button { Text = "Vincular hábitos e tarefas" };
        link.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new GoalLinksPage(model, goalId)));
        content.Add(add); content.Add(link);
        if (goal.Kind == GoalKind.BodyWeight)
        {
            content.Add(new Label { Text = "Histórico de pesagens", FontSize = 20, FontAttributes = FontAttributes.Bold });
            var body = new Button { Text = "Ver evolução, corrigir e registrar medidas" };
            body.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new BodyPage(model)));
            content.Add(body);
            foreach (var weight in model.Weights.Reverse())
                content.Add(PageLayout.Card(new Label { Text = $"{GoalUi.Number(weight.Kilograms)} kg", FontSize = 19 },
                    PageLayout.Muted(weight.MeasuredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm") +
                        (weight.VoidedAt is null ? "" : " • corrigido ou desconsiderado")),
                    PageLayout.Muted(weight.Note ?? "")));
        }
        else
        {
            content.Add(new Label { Text = "Histórico de progresso", FontSize = 20, FontAttributes = FontAttributes.Bold });
            foreach (var item in (await model.ProgressAsync(goalId)).Reverse())
                content.Add(PageLayout.Card(new Label { Text = $"{GoalUi.Number(item.Value)} {goal.Unit}", FontSize = 19 },
                    PageLayout.Muted(item.RecordedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")),
                    PageLayout.Muted(item.Note ?? "")));
        }
    }
    private async Task AddProgressAsync(Goal goal)
    {
        var input = await DisplayPromptAsync("Registrar progresso", $"Valor atual em {goal.Unit}", "Continuar", "Cancelar", keyboard: Keyboard.Numeric);
        if (input is null) return;
        var note = await DisplayPromptAsync("Nota opcional", "O que mudou?", "Salvar", "Cancelar");
        if (note is null) return;
        try { await model.AddProgressAsync(goal, GoalUi.Parse(input, "o progresso"), note); await RenderAsync(); }
        catch (Exception ex) { await DisplayAlert("Valor inválido", ex.Message, "Entendi"); }
    }
}

public sealed class WeightEditorPage : ContentPage
{
    private readonly GoalViewModel model;
    private readonly Func<Task> saved;
    private readonly Entry kg = new() { Placeholder = "Peso em kg", Keyboard = Keyboard.Numeric };
    private readonly DatePicker date = new() { Format = "dd/MM/yyyy" };
    private readonly TimePicker time = new() { Time = DateTime.Now.TimeOfDay };
    private readonly Entry note = new() { Placeholder = "Nota opcional", MaxLength = 1000 };
    private readonly Label error = PageLayout.Muted("");
    public WeightEditorPage(GoalViewModel model, Func<Task> saved, BodyWeightRecord? existing = null)
    {
        this.model = model; this.saved = saved;
        Title = existing is null ? "Registrar pesagem" : "Corrigir pesagem";
        if (existing is not null)
        {
            kg.Text = GoalUi.Number(existing.Kilograms);
            var local = existing.MeasuredAt.ToLocalTime();
            date.Date = local.Date;
            time.Time = local.TimeOfDay;
            note.Text = existing.Note;
        }
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        var save = new Button { Text = existing is null ? "Salvar pesagem" : "Salvar correção" };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var value = GoalUi.Parse(kg.Text, "o peso");
                var at = PlanningUi.LocalInstant(date.Date, time.Time);
                if (existing is null) await model.AddWeightAsync(value, at, note.Text);
                else await model.CorrectWeightAsync(existing, value, at, note.Text);
                await Navigation.PopModalAsync();
                await saved();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading("Pesagem"),
            PageLayout.Muted("Cada pesagem fica no histórico independente da meta."),
            PageLayout.Card(new Label { Text = "Peso em kg" }, kg, new Label { Text = "Data" }, date,
                new Label { Text = "Hora" }, time, new Label { Text = "Nota" }, note, error, save));
    }
}

public sealed class GoalLinksPage : ContentPage
{
    private readonly GoalViewModel model;
    private readonly Guid goalId;
    private readonly VerticalStackLayout content = new() { Spacing = 14 };
    public GoalLinksPage(GoalViewModel model, Guid goalId)
    {
        this.model = model; this.goalId = goalId;
        Title = "Vínculos da meta";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        Content = new ScrollView { Content = content };
        content.Padding = new Thickness(24, 20, 24, 32);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await model.LoadAsync(); await RenderAsync(); }
    private async Task RenderAsync()
    {
        content.Clear();
        content.Add(PageLayout.Heading("Hábitos e tarefas"));
        content.Add(PageLayout.Muted("Vincular não muda o estado do item nem apaga seu histórico."));
        var links = await model.LinksAsync(goalId);
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var habit in model.Habits.Where(h => h.IsActiveOn(today)))
        {
            var link = new GoalLink(goalId, GoalLinkKind.Habit, habit.Id);
            var linked = links.Contains(link);
            var button = new Button { Text = linked ? "Desvincular" : "Vincular" };
            button.Clicked += async (_, _) => { if (linked) await model.RemoveLinkAsync(link); else await model.AddLinkAsync(link); await RenderAsync(); };
            content.Add(PageLayout.Card(new Label { Text = habit.RevisionOn(today)?.Name ?? "Hábito" }, button));
        }
        foreach (var task in model.Tasks.Where(t => !t.IsArchived))
        {
            var link = new GoalLink(goalId, GoalLinkKind.Task, task.Id);
            var linked = links.Contains(link);
            var button = new Button { Text = linked ? "Desvincular" : "Vincular" };
            button.Clicked += async (_, _) => { if (linked) await model.RemoveLinkAsync(link); else await model.AddLinkAsync(link); await RenderAsync(); };
            content.Add(PageLayout.Card(new Label { Text = task.Title }, button));
        }
        if (model.Habits.Count == 0 && model.Tasks.Count == 0) content.Add(PageLayout.Muted("Crie um hábito ou tarefa para vinculá-lo à meta."));
    }
}
