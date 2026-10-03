using System.Globalization;
using Rotina.Domain;

namespace Rotina.Mobile;

internal static class PlanningUi
{
    public static readonly string[] ReminderChoices = ["Sem lembrete", "Na hora", "15 min antes", "1 hora antes", "1 dia antes"];
    public static DateTimeOffset LocalInstant(DateTime date, TimeSpan time)
    {
        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
    public static DateTimeOffset? Reminder(DateTimeOffset starts, int selected) => selected switch
    {
        1 => starts,
        2 => starts.AddMinutes(-15),
        3 => starts.AddHours(-1),
        4 => starts.AddDays(-1),
        _ => null
    };
    public static int ReminderIndex(DateTimeOffset starts, DateTimeOffset? reminder)
    {
        if (reminder is null) return 0;
        var minutes = (starts - reminder.Value).TotalMinutes;
        return minutes < 8 ? 1 : minutes < 38 ? 2 : minutes < 750 ? 3 : 4;
    }
    public static Picker AreaPicker(IReadOnlyList<LifeArea> areas, Guid? selected)
    {
        var picker = new Picker { Title = "Área da vida" };
        picker.Items.Add("Sem área");
        foreach (var area in areas) picker.Items.Add(area.Name);
        picker.SelectedIndex = selected is { } id ? Math.Max(0, 1 + areas.ToList().FindIndex(a => a.Id == id)) : 0;
        return picker;
    }
    public static Guid? SelectedArea(Picker picker, IReadOnlyList<LifeArea> areas) =>
        picker.SelectedIndex > 0 && picker.SelectedIndex <= areas.Count ? areas[picker.SelectedIndex - 1].Id : null;
}

public sealed class TaskEditorPage : ContentPage
{
    private readonly PlanningViewModel model;
    private readonly PlannedTask? existing;
    private readonly Func<Task> saved;
    private readonly Entry title = new() { Placeholder = "Ex.: Pagar conta", MaxLength = 120 };
    private readonly Editor description = new() { Placeholder = "Descrição opcional", HeightRequest = 100, MaxLength = 2000 };
    private readonly Switch hasDue = new();
    private readonly DatePicker due = new() { Format = "dd/MM/yyyy" };
    private readonly TimePicker dueTime = new() { Time = new TimeSpan(9, 0, 0) };
    private readonly Picker priority = new() { Title = "Prioridade", ItemsSource = new[] { "Baixa", "Normal", "Alta" } };
    private readonly Picker reminder = new() { Title = "Lembrete", ItemsSource = PlanningUi.ReminderChoices };
    private readonly VerticalStackLayout dueFields = new() { Spacing = 14 };
    private readonly Picker area;
    private readonly Label error = PageLayout.Muted("");

    public TaskEditorPage(PlanningViewModel model, PlannedTask? existing, Func<Task> saved)
    {
        this.model = model; this.existing = existing; this.saved = saved;
        Title = existing is null ? "Nova tarefa" : "Editar tarefa";
        title.Text = existing?.Title ?? "";
        description.Text = existing?.Description ?? "";
        hasDue.IsToggled = existing?.DueOn is not null;
        due.Date = existing?.DueOn?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        priority.SelectedIndex = (int)(existing?.Priority ?? TaskPriority.Normal);
        var baseTime = PlanningUi.LocalInstant(due.Date, dueTime.Time);
        reminder.SelectedIndex = PlanningUi.ReminderIndex(baseTime, existing?.ReminderAt);
        area = PlanningUi.AreaPicker(model.Areas.Where(a => !a.IsArchived || a.Id == existing?.AreaId).ToList(), existing?.AreaId);
        dueFields.Add(due);
        dueFields.Add(new Label { Text = "Horário de referência para o lembrete" });
        dueFields.Add(dueTime);
        dueFields.Add(new Label { Text = "Avisar" });
        dueFields.Add(reminder);
        hasDue.Toggled += (_, _) => dueFields.IsVisible = hasDue.IsToggled;
        dueFields.IsVisible = hasDue.IsToggled;
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        var save = new Button { Text = "Salvar tarefa" };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var activeAreas = model.Areas.Where(a => !a.IsArchived || a.Id == existing?.AreaId).ToList();
                var dueOn = hasDue.IsToggled ? DateOnly.FromDateTime(due.Date) : (DateOnly?)null;
                var dueInstant = PlanningUi.LocalInstant(due.Date, dueTime.Time);
                var item = new PlannedTask(existing?.Id ?? Guid.NewGuid(), title.Text ?? "", description.Text,
                    dueOn, (TaskPriority)priority.SelectedIndex, PlanningUi.SelectedArea(area, activeAreas),
                    dueOn is null ? null : PlanningUi.Reminder(dueInstant, reminder.SelectedIndex), existing?.CompletedAt,
                    existing?.IsArchived ?? false);
                await model.SaveTaskAsync(item);
#if ANDROID
                if (item.ReminderAt is not null && !await AndroidReminders.AskPermissionAsync())
                    await DisplayAlert("Lembrete desativado", "A tarefa foi salva, mas o Android não autorizou notificações.", "Entendi");
                AndroidReminders.Sync(item);
#endif
                await Navigation.PopModalAsync();
                await saved();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading(Title),
            PageLayout.Muted("Uma tarefa é pontual. Ao concluir, ela não reaparece automaticamente."),
            PageLayout.Card(new Label { Text = "Título" }, title, new Label { Text = "Descrição" }, description,
                new HorizontalStackLayout { Spacing = 12, Children = { hasDue, new Label { Text = "Definir prazo", VerticalOptions = LayoutOptions.Center } } },
                dueFields,
                new Label { Text = "Prioridade" }, priority, new Label { Text = "Área" }, area,
                error, save));
    }
}

public sealed class AppointmentEditorPage : ContentPage
{
    private readonly PlanningViewModel model;
    private readonly Appointment? existing;
    private readonly Func<Task> saved;
    private readonly Entry title = new() { Placeholder = "Ex.: Dentista", MaxLength = 120 };
    private readonly DatePicker date = new() { Format = "dd/MM/yyyy" };
    private readonly TimePicker time = new() { Time = new TimeSpan(9, 0, 0) };
    private readonly Entry duration = new() { Placeholder = "Minutos", Keyboard = Keyboard.Numeric };
    private readonly Entry location = new() { Placeholder = "Local opcional", MaxLength = 300 };
    private readonly Editor note = new() { Placeholder = "Nota opcional", HeightRequest = 100, MaxLength = 2000 };
    private readonly Picker reminder = new() { Title = "Lembrete", ItemsSource = PlanningUi.ReminderChoices };
    private readonly Picker area;
    private readonly Label error = PageLayout.Muted("");

    public AppointmentEditorPage(PlanningViewModel model, Appointment? existing, Func<Task> saved)
    {
        this.model = model; this.existing = existing; this.saved = saved;
        Title = existing is null ? "Novo compromisso" : "Editar compromisso";
        title.Text = existing?.Title ?? "";
        date.Date = existing?.StartsAt.ToLocalTime().DateTime.Date ?? DateTime.Today;
        time.Time = existing?.StartsAt.ToLocalTime().TimeOfDay ?? new TimeSpan(9, 0, 0);
        duration.Text = (existing?.DurationMinutes ?? 60).ToString();
        location.Text = existing?.Location ?? "";
        note.Text = existing?.Note ?? "";
        reminder.SelectedIndex = PlanningUi.ReminderIndex(existing?.StartsAt ?? PlanningUi.LocalInstant(date.Date, time.Time), existing?.ReminderAt);
        area = PlanningUi.AreaPicker(model.Areas.Where(a => !a.IsArchived || a.Id == existing?.AreaId).ToList(), existing?.AreaId);
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        var save = new Button { Text = "Salvar compromisso" };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                if (!int.TryParse(duration.Text, out var minutes)) throw new ArgumentException("Informe a duração em minutos.");
                var starts = PlanningUi.LocalInstant(date.Date, time.Time);
                var activeAreas = model.Areas.Where(a => !a.IsArchived || a.Id == existing?.AreaId).ToList();
                var item = new Appointment(existing?.Id ?? Guid.NewGuid(), title.Text ?? "", starts, minutes,
                    location.Text, note.Text, PlanningUi.SelectedArea(area, activeAreas),
                    PlanningUi.Reminder(starts, reminder.SelectedIndex),
                    existing?.IsArchived ?? false);
                await model.SaveAppointmentAsync(item);
#if ANDROID
                if (item.ReminderAt is not null && !await AndroidReminders.AskPermissionAsync())
                    await DisplayAlert("Lembrete desativado", "O compromisso foi salvo, mas o Android não autorizou notificações.", "Entendi");
                AndroidReminders.Sync(item);
#endif
                await Navigation.PopModalAsync();
                await saved();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading(Title),
            PageLayout.Card(new Label { Text = "Título" }, title, new Label { Text = "Data" }, date,
                new Label { Text = "Hora" }, time, new Label { Text = "Duração em minutos" }, duration,
                new Label { Text = "Local" }, location, new Label { Text = "Nota" }, note,
                new Label { Text = "Área" }, area, new Label { Text = "Avisar" }, reminder, error, save));
    }
}

public sealed class PlanningPage : ContentPage
{
    private readonly PlanningViewModel model;
    private readonly VerticalStackLayout list = new() { Spacing = 14 };
    private readonly DatePicker day = new() { Format = "dd/MM/yyyy" };
    private readonly Switch showArchived = new();
    private readonly Label heading = PageLayout.Heading("Agenda");
    private bool calendarOnly;

    public PlanningPage(PlanningViewModel model, bool calendarOnly = false)
    {
        this.model = model; this.calendarOnly = calendarOnly;
        Title = calendarOnly ? "Calendário" : "Tarefas e compromissos";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        var addTask = new Button { Text = "+ Tarefa" };
        addTask.Clicked += async (_, _) => await EditTaskAsync(null);
        var addEvent = new Button { Text = "+ Compromisso" };
        addEvent.Clicked += async (_, _) => await EditAppointmentAsync(null);
        var month = new Button { Text = "Ver mês" };
        month.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new CalendarPage(model, day.Date)));
        day.DateSelected += (_, _) => Render();
        showArchived.Toggled += (_, _) => Render();
        Content = PageLayout.Body(heading, PageLayout.Muted("Planeje o dia sem perder o histórico."),
            new HorizontalStackLayout { Spacing = 10, Children = { addTask, addEvent } },
            PageLayout.Card(new Label { Text = "Dia" }, day, month),
            new HorizontalStackLayout { Spacing = 10, Children = { showArchived,
                new Label { Text = "Mostrar arquivados", VerticalOptions = LayoutOptions.Center } } }, list);
    }
    public DateTime SelectedDate { set => day.Date = value; }
    protected override async void OnAppearing() { base.OnAppearing(); await RefreshAsync();
#if ANDROID
        AndroidReminders.SyncAll(model);
#endif
    }
    private async Task RefreshAsync() { await model.LoadAsync(); Render(); }
    private void Render()
    {
        list.Clear();
        if (model.Error is { } error) { list.Add(PageLayout.Muted(error)); return; }
        var selected = DateOnly.FromDateTime(day.Date);
        var tasks = model.Tasks.Where(t => !t.IsArchived && t.DueOn == selected).ToList();
        var events = model.Appointments.Where(a => !a.IsArchived && DateOnly.FromDateTime(a.StartsAt.ToLocalTime().DateTime) == selected).ToList();
        if (tasks.Count == 0 && events.Count == 0) list.Add(PageLayout.Muted("Nada planejado para este dia."));
        if (tasks.Count > 0)
        {
            list.Add(new Label { Text = "Tarefas", FontSize = 22, FontAttributes = FontAttributes.Bold });
            foreach (var task in tasks) list.Add(TaskCard(task));
        }
        if (events.Count > 0)
        {
            list.Add(new Label { Text = "Compromissos", FontSize = 22, FontAttributes = FontAttributes.Bold });
            foreach (var appointment in events) list.Add(AppointmentCard(appointment));
        }
        if (!calendarOnly)
        {
            var noDate = model.Tasks.Where(t => !t.IsArchived && t.DueOn is null).ToList();
            if (noDate.Count > 0)
            {
                list.Add(new Label { Text = "Sem prazo", FontSize = 22, FontAttributes = FontAttributes.Bold });
                foreach (var task in noDate) list.Add(TaskCard(task));
            }
        }
        if (showArchived.IsToggled)
        {
            var archivedTasks = model.Tasks.Where(t => t.IsArchived).ToList();
            var archivedEvents = model.Appointments.Where(a => a.IsArchived).ToList();
            if (archivedTasks.Count + archivedEvents.Count == 0) list.Add(PageLayout.Muted("Nenhum item arquivado."));
            else
            {
                list.Add(new Label { Text = "Arquivados", FontSize = 22, FontAttributes = FontAttributes.Bold });
                foreach (var task in archivedTasks) list.Add(TaskCard(task));
                foreach (var item in archivedEvents) list.Add(AppointmentCard(item));
            }
        }
    }
    private View TaskCard(PlannedTask item)
    {
        var toggle = new Button { Text = item.CompletedAt is null ? "Concluir" : "Reabrir" };
        toggle.Clicked += async (_, _) => { try { await model.CompleteTaskAsync(item);
#if ANDROID
            AndroidReminders.Sync(model.Tasks.First(t => t.Id == item.Id));
#endif
            Render(); } catch (Exception ex) { await DisplayAlert("Erro", ex.Message, "Entendi"); } };
        var edit = new Button { Text = "Editar" };
        edit.Clicked += async (_, _) => await EditTaskAsync(item);
        var archive = new Button { Text = item.IsArchived ? "Restaurar" : "Arquivar" };
        archive.Clicked += async (_, _) => { if (item.IsArchived || await DisplayAlert("Arquivar tarefa?", "Ela ficará no histórico.", "Arquivar", "Cancelar")) { await model.ArchiveTaskAsync(item, !item.IsArchived);
#if ANDROID
            AndroidReminders.Sync(model.Tasks.First(t => t.Id == item.Id));
#endif
            Render(); } };
        return PageLayout.Card(new Label { Text = item.Title, FontSize = 19, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted($"{(item.CompletedAt is null ? "Pendente" : "Concluída")} • prioridade {new[] { "baixa", "normal", "alta" }[(int)item.Priority]}"),
            PageLayout.Muted(item.Description ?? ""), toggle, edit, archive);
    }
    private View AppointmentCard(Appointment item)
    {
        var edit = new Button { Text = "Editar" };
        edit.Clicked += async (_, _) => await EditAppointmentAsync(item);
        var calendar = new Button { Text = "Adicionar ao calendário Android" };
        calendar.Clicked += async (_, _) =>
        {
#if ANDROID
            try { AndroidCalendar.OpenInsert(item); }
            catch (Exception ex) { await DisplayAlert("Calendário indisponível", ex.Message, "Entendi"); }
#endif
        };
        var archive = new Button { Text = item.IsArchived ? "Restaurar" : "Arquivar" };
        archive.Clicked += async (_, _) => { if (item.IsArchived || await DisplayAlert("Arquivar compromisso?", "Ele ficará no histórico do app.", "Arquivar", "Cancelar")) { await model.ArchiveAppointmentAsync(item, !item.IsArchived);
#if ANDROID
            AndroidReminders.Sync(model.Appointments.First(a => a.Id == item.Id));
#endif
            Render(); } };
        return PageLayout.Card(new Label { Text = item.Title, FontSize = 19, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted($"{item.StartsAt.ToLocalTime():HH:mm} • {item.DurationMinutes} min"),
            PageLayout.Muted(item.Location ?? item.Note ?? ""), edit, calendar, archive);
    }
    private async Task EditTaskAsync(PlannedTask? item) => await Navigation.PushModalAsync(new NavigationPage(new TaskEditorPage(model, item, RefreshAsync)));
    private async Task EditAppointmentAsync(Appointment? item) => await Navigation.PushModalAsync(new NavigationPage(new AppointmentEditorPage(model, item, RefreshAsync)));
}

public sealed class CalendarPage : ContentPage
{
    private readonly PlanningViewModel model;
    private readonly VerticalStackLayout dates = new() { Spacing = 8 };
    private DateTime month;
    public CalendarPage(PlanningViewModel model, DateTime selected)
    {
        this.model = model; month = new DateTime(selected.Year, selected.Month, 1);
        Title = "Calendário";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        var back = new Button { Text = "‹" };
        back.Clicked += (_, _) => { month = month.AddMonths(-1); Render(); };
        var next = new Button { Text = "›" };
        next.Clicked += (_, _) => { month = month.AddMonths(1); Render(); };
        Content = PageLayout.Body(PageLayout.Heading("Calendário"),
            new HorizontalStackLayout { Spacing = 12, Children = { back, next } }, dates);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await model.LoadAsync(); Render(); }
    private void Render()
    {
        dates.Clear();
        dates.Add(new Label { Text = month.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("pt-BR")), FontSize = 23, FontAttributes = FontAttributes.Bold });
        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < 7; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var weekdays = new[] { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" };
        for (var i = 0; i < 7; i++) grid.Add(new Label { Text = weekdays[i], HorizontalTextAlignment = TextAlignment.Center, FontSize = 12 }, i, 0);
        var offset = ((int)month.DayOfWeek + 6) % 7;
        for (var day = month; day.Month == month.Month; day = day.AddDays(1))
        {
            var current = day;
            var dateOnly = DateOnly.FromDateTime(day);
            var count = model.Appointments.Count(a => !a.IsArchived && DateOnly.FromDateTime(a.StartsAt.ToLocalTime().DateTime) == dateOnly);
            var tasks = model.Tasks.Count(t => !t.IsArchived && t.DueOn == dateOnly);
            var position = offset + day.Day - 1;
            var button = new Button { Text = count + tasks > 0 ? $"{day.Day}\n• {count + tasks}" : day.Day.ToString(),
                FontSize = 13, Padding = new Thickness(1), HeightRequest = 50 };
            button.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new PlanningPage(model, true) { SelectedDate = current }));
            grid.Add(button, position % 7, 1 + position / 7);
        }
        dates.Add(grid);
        dates.Add(PageLayout.Muted("O ponto indica quantos itens há no dia. Toque em uma data para abrir a agenda."));
    }
}
