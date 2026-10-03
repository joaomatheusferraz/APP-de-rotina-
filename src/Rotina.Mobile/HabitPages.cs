using System.Globalization;
using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class RoutinePage : ContentPage
{
    private readonly HabitViewModel model;
    private readonly PlanningViewModel planning;
    private readonly GoalViewModel goals;
    private readonly FocusViewModel focus;
    private readonly IDreamBoardStore dreams;
    private readonly VerticalStackLayout list = new() { Spacing = 14 };
    private readonly Label status = PageLayout.Muted("");

    public RoutinePage(HabitViewModel model, PlanningViewModel planning, GoalViewModel goals, FocusViewModel focus, IDreamBoardStore dreams)
    {
        this.model = model;
        this.planning = planning;
        this.goals = goals;
        this.focus = focus;
        this.dreams = dreams;
        Title = "Rotina";
        var add = PageLayout.Primary("+ Criar hábito");
        add.Clicked += async (_, _) => await EditAsync(null);
        var plan = new Button { Text = "Tarefas e compromissos" };
        plan.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new PlanningPage(planning)));
        var calendar = new Button { Text = "Calendário" };
        calendar.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new CalendarPage(planning, DateTime.Today)));
        var goalButton = new Button { Text = "Metas e Plano de Ação" };
        goalButton.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new GoalsPage(goals)));
        var focusButton = new Button { Text = "Abrir Modo Foco" };
        focusButton.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new FocusPage(focus)));
        var dreamsButton = new Button { Text = "Abrir Quadro dos Sonhos" };
        dreamsButton.Clicked += async (_, _) => { await goals.LoadAsync(); await Navigation.PushModalAsync(new NavigationPage(new DreamBoardPage(dreams, goals))); };
        Content = PageLayout.Body(PageLayout.Hero("FOCO • EQUILÍBRIO", "Construa seu ritmo.", "Transforme pequenas ações em hábitos que ficam."),
            add, status, list, dreamsButton,
            PageLayout.Card(new Label { Text = "Planejamento", FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted("Tarefas, compromissos, calendário, foco, sonhos e metas."), plan, calendar, focusButton, goalButton));
    }

    protected override async void OnAppearing() { base.OnAppearing(); await RefreshAsync(); }

    private async Task RefreshAsync()
    {
        await model.LoadAsync();
        status.Text = model.Error ?? "";
        list.Clear();
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (model.All.Count == 0) { list.Add(PageLayout.Muted("Você ainda não criou hábitos. Comece por uma ação pequena.")); return; }
        foreach (var history in model.All.OrderBy(h => h.ArchivedOn is not null).ThenBy(h => h.RevisionOn(today)?.Name))
        {
            var revision = history.RevisionOn(today) ?? history.Revisions.LastOrDefault();
            if (revision is null) continue;
            var report = HabitAnalytics.Build(history, today);
            var description = revision.IsPaused ? "Pausado" : history.ArchivedOn is not null ? "Arquivado" :
                revision.Frequency switch
                {
                    HabitFrequency.Daily => "Todos os dias",
                    HabitFrequency.Weekdays => "Dias escolhidos",
                    _ => $"{revision.WeeklyTarget} vezes por semana"
                };
            var detail = new Button { Text = "Ver histórico" };
            detail.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new HabitDetailPage(model, history.Id)));
            var edit = new Button { Text = history.ArchivedOn is null ? "Editar programação" : "Restaurar" };
            edit.Clicked += async (_, _) =>
            {
                if (history.ArchivedOn is not null)
                {
                    try { await model.SetArchivedAsync(history, false); await RefreshAsync(); }
                    catch (Exception ex) { await DisplayAlert("Não foi possível restaurar", ex.Message, "Entendi"); }
                }
                else await EditAsync(history);
            };
            var archive = new Button { Text = "Arquivar" };
            archive.Clicked += async (_, _) =>
            {
                if (!await DisplayAlert("Arquivar hábito?", "O histórico continuará disponível e você poderá restaurar este hábito.", "Arquivar", "Cancelar")) return;
                try { await model.SetArchivedAsync(history, true); await RefreshAsync(); }
                catch (Exception ex) { await DisplayAlert("Não foi possível arquivar", ex.Message, "Entendi"); }
            };
            list.Add(PageLayout.Card(
                new Label { Text = revision.Name, FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted(description),
                PageLayout.Muted($"7 dias: {report.Last7.Done}/{report.Last7.Planned} • Sequência: {report.CurrentStreak}"),
                detail, edit, archive));
            archive.IsVisible = history.ArchivedOn is null;
        }
    }

    private async Task EditAsync(HabitHistory? history)
    {
        await model.LoadAsync();
        var page = new HabitEditorPage(model, history, async () => await RefreshAsync());
        await Navigation.PushModalAsync(new NavigationPage(page));
    }
}

public sealed class HabitEditorPage : ContentPage
{
    private readonly HabitViewModel model;
    private readonly HabitHistory? history;
    private readonly Func<Task> saved;
    private readonly Entry name = new() { Placeholder = "Ex.: Tomar creatina", MaxLength = 100 };
    private readonly Picker area = new() { Title = "Sem área" };
    private readonly Picker frequency = new() { Title = "Frequência", ItemsSource = new[] { "Todos os dias", "Dias da semana", "X vezes por semana" } };
    private readonly Picker target = new() { Title = "Vezes por semana", ItemsSource = Enumerable.Range(1, 7).Select(i => i.ToString()).ToList() };
    private readonly DatePicker startDate = new() { Format = "dd/MM/yyyy" };
    private readonly VerticalStackLayout days = new() { Spacing = 5 };
    private readonly List<(DayOfWeek day, CheckBox box)> dayBoxes = [];
    private readonly Switch paused = new();
    private readonly Label error = PageLayout.Muted("");
    private readonly Button save = new() { Text = "Salvar hábito" };

    public HabitEditorPage(HabitViewModel model, HabitHistory? history, Func<Task> saved)
    {
        this.model = model;
        this.history = history;
        this.saved = saved;
        Title = history is null ? "Novo hábito" : "Editar hábito";
        var today = DateOnly.FromDateTime(DateTime.Now);
        var current = history?.RevisionOn(today) ?? history?.Revisions.LastOrDefault();
        name.Text = current?.Name ?? "";
        startDate.MinimumDate = history is null ? new DateTime(2020, 1, 1) : DateTime.Today;
        startDate.MaximumDate = DateTime.Today.AddYears(5);
        startDate.Date = history?.CreatedOn.ToDateTime(TimeOnly.MinValue) is { } created && created > DateTime.Today
            ? created : DateTime.Today;
        var areas = model.Areas.Where(a => !a.IsArchived || a.Id == current?.AreaId).ToList();
        area.Items.Add("Sem área");
        foreach (var item in areas) area.Items.Add(item.Name);
        area.SelectedIndex = current?.AreaId is { } areaId ? 1 + areas.FindIndex(a => a.Id == areaId) : 0;
        frequency.SelectedIndex = (int)(current?.Frequency ?? HabitFrequency.Daily);
        target.SelectedIndex = (current?.WeeklyTarget ?? 1) - 1;
        paused.IsToggled = current?.IsPaused ?? false;
        var names = new[] { "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado" };
        for (var i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)i;
            var check = new CheckBox { IsChecked = current?.Frequency == HabitFrequency.Weekdays && (current.WeekdayMask & (1 << i)) != 0 };
            var row = new HorizontalStackLayout { Spacing = 12, Children = { check, new Label { Text = names[i], VerticalOptions = LayoutOptions.Center } } };
            days.Add(row);
            dayBoxes.Add((day, check));
        }
        frequency.SelectedIndexChanged += (_, _) => ShowScheduleFields();
        ShowScheduleFields();
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        save.Clicked += OnSave;
        Content = PageLayout.Body(PageLayout.Heading(Title),
            PageLayout.Muted(history is null
                ? "Escolha quando começar. Você poderá ajustar a programação sem perder o histórico."
                : "A nova programação começa na data escolhida. Versões anteriores continuam guardadas."),
            PageLayout.Card(new Label { Text = "Nome" }, name, new Label { Text = "Área" }, area,
                new Label { Text = history is null ? "Data de início" : "Nova programação a partir de" }, startDate,
                new Label { Text = "Frequência" }, frequency, days, target,
                new HorizontalStackLayout { Spacing = 12, Children = { paused, new Label { Text = "Pausar hábito", VerticalOptions = LayoutOptions.Center } } },
                error, save));
    }

    private void ShowScheduleFields()
    {
        days.IsVisible = frequency.SelectedIndex == (int)HabitFrequency.Weekdays;
        target.IsVisible = frequency.SelectedIndex == (int)HabitFrequency.TimesPerWeek;
    }

    private async void OnSave(object? sender, EventArgs e)
    {
        save.IsEnabled = false;
        try
        {
            var mask = dayBoxes.Where(item => item.box.IsChecked).Aggregate(0, (value, item) => value | (1 << (int)item.day));
            var areas = model.Areas.Where(a => !a.IsArchived || a.Id == history?.RevisionOn(DateOnly.FromDateTime(DateTime.Now))?.AreaId).ToList();
            var areaId = area.SelectedIndex > 0 && area.SelectedIndex <= areas.Count ? areas[area.SelectedIndex - 1].Id : (Guid?)null;
            await model.SaveAsync(history?.Id, name.Text ?? "", areaId, DateOnly.FromDateTime(startDate.Date), (HabitFrequency)frequency.SelectedIndex,
                mask, target.SelectedIndex + 1, paused.IsToggled);
            await Navigation.PopModalAsync();
            await saved();
        }
        catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
    }
}

public sealed class HabitDetailPage : ContentPage
{
    private readonly HabitViewModel model;
    private readonly Guid habitId;
    private readonly VerticalStackLayout content = new() { Spacing = 20 };

    public HabitDetailPage(HabitViewModel model, Guid habitId)
    {
        this.model = model;
        this.habitId = habitId;
        Title = "Histórico do hábito";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        Content = new ScrollView { Content = content };
        content.Padding = new Thickness(24, 20, 24, 32);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await model.LoadAsync();
        Render();
    }

    private void Render()
    {
        content.Clear();
        var history = model.All.FirstOrDefault(h => h.Id == habitId);
        if (history is null) { content.Add(PageLayout.Muted("Hábito indisponível.")); return; }
        var today = DateOnly.FromDateTime(DateTime.Now);
        var report = HabitAnalytics.Build(history, today);
        content.Add(PageLayout.Heading(history.RevisionOn(today)?.Name ?? "Hábito"));
        content.Add(PageLayout.Card(new Label { Text = "Consistência", FontSize = 21, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted($"7 dias: {Format(report.Last7)}"),
            PageLayout.Muted($"30 dias: {Format(report.Last30)}"),
            PageLayout.Muted($"90 dias: {Format(report.Last90)}"),
            PageLayout.Muted($"Este mês: {Format(HabitAnalytics.Score(history, today, today.Day))}"),
            PageLayout.Muted($"Sequência atual: {report.CurrentStreak} • Melhor: {report.BestStreak}")));
        content.Add(new Label { Text = "Calendário do mês", FontSize = 21, FontAttributes = FontAttributes.Bold });
        content.Add(MonthCalendar(history, today));
        content.Add(new Label { Text = "Últimos 30 dias", FontSize = 21, FontAttributes = FontAttributes.Bold });
        for (var day = today; day >= today.AddDays(-29); day = day.AddDays(-1))
        {
            var state = HabitAnalytics.Day(history, day, today);
            var label = state switch
            {
                HabitDayState.Completed => "Concluído",
                HabitDayState.Missed => "Não realizado",
                HabitDayState.ScheduledPending => "Programado",
                HabitDayState.Rest => "Descanso / sem programação",
                HabitDayState.Paused => "Pausado",
                HabitDayState.Archived => "Arquivado",
                _ => "Ainda não iniciado"
            };
            var note = history.Events.LastOrDefault(e => e.Date == day && e.IsCompleted)?.Note;
            var card = PageLayout.Card(new Label { Text = day.ToString("ddd, dd MMM", CultureInfo.GetCultureInfo("pt-BR")), FontAttributes = FontAttributes.Bold },
                PageLayout.Muted(label), PageLayout.Muted(note ?? ""));
            if (state == HabitDayState.Completed)
            {
                var noteButton = new Button { Text = "Adicionar nota" };
                var occurrence = day;
                noteButton.Clicked += async (_, _) =>
                {
                    var entry = await DisplayPromptAsync("Nota do hábito", "O que vale registrar sobre esta conclusão?", "Salvar", "Cancelar", maxLength: 500, initialValue: note ?? "");
                    if (string.IsNullOrWhiteSpace(entry)) return;
                    try { await model.AddNoteAsync(history, occurrence, entry); await ReloadAsync(); }
                    catch (Exception ex) { await DisplayAlert("Não foi possível salvar a nota", ex.Message, "Entendi"); }
                };
                if (card.Content is VerticalStackLayout stack) stack.Add(noteButton);
            }
            content.Add(card);
        }
    }

    private async Task ReloadAsync()
    {
        await model.LoadAsync();
        Render();
    }

    private static View MonthCalendar(HabitHistory history, DateOnly today)
    {
        var first = new DateOnly(today.Year, today.Month, 1);
        var grid = new Grid { ColumnSpacing = 5, RowSpacing = 5 };
        for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        var labels = new[] { "S", "T", "Q", "Q", "S", "S", "D" };
        for (var i = 0; i < 7; i++) grid.Add(new Label { Text = labels[i], HorizontalTextAlignment = TextAlignment.Center, FontSize = 12 }, i, 0);
        var offset = ((int)first.DayOfWeek + 6) % 7;
        for (var n = 0; n < DateTime.DaysInMonth(today.Year, today.Month); n++)
        {
            var day = first.AddDays(n);
            var state = day > today ? HabitDayState.BeforeStart : HabitAnalytics.Day(history, day, today);
            var color = state switch
            {
                HabitDayState.Completed => Color.FromArgb("#42624C"),
                HabitDayState.Missed => Color.FromArgb("#754C4C"),
                HabitDayState.ScheduledPending => Color.FromArgb("#5F6250"),
                _ => Color.FromArgb("#293630")
            };
            var cell = new Border
            {
                BackgroundColor = color, Padding = new Thickness(0, 8),
                StrokeThickness = 0,
                Content = new Label { Text = day.Day.ToString(), HorizontalTextAlignment = TextAlignment.Center, TextColor = Colors.White, FontSize = 13 }
            };
            grid.Add(cell, (offset + n) % 7, 1 + (offset + n) / 7);
        }
        return PageLayout.Card(grid, PageLayout.Muted("Verde: concluído • Vermelho: programado e perdido • Cinza: descanso, pausa ou sem programação"));
    }
    private static string Format(HabitScore score) => score.Planned == 0 ? "sem programação" :
        $"{score.Done}/{score.Planned} ({score.Fraction:P0})";
}
