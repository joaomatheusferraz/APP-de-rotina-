using System.Globalization;
using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

internal static class PageLayout
{
    public static Label Heading(string text) => new() { Text = text, FontSize = 32, CharacterSpacing = -0.8, FontFamily = "OpenSansSemibold" };
    public static Button Primary(string text) => new() { Text = text,
        Style = (Style)Microsoft.Maui.Controls.Application.Current!.Resources["PrimaryButton"] };
    public static Border Hero(string eyebrow, string title, string description)
    {
        var card = Card(new Label { Text = eyebrow, FontSize = 11, CharacterSpacing = 2.5, TextColor = Color.FromArgb("#66DAEB") },
            new Label { Text = title, FontSize = 30, FontFamily = "OpenSansSemibold", CharacterSpacing = -0.7, TextColor = Colors.White },
            new Label { Text = description, FontSize = 14, TextColor = Color.FromArgb("#C0CBE6") });
        card.Background = new LinearGradientBrush(new GradientStopCollection {
            new GradientStop(Color.FromArgb("#27254D"), 0), new GradientStop(Color.FromArgb("#102D43"), 1) }, new Point(0, 0), new Point(1, 1));
        card.Stroke = Color.FromArgb("#4A5279");
        card.Padding = 24;
        return card;
    }
    public static Label Muted(string text)
    {
        var label = new Label { Text = text, FontSize = 15, LineHeight = 1.3 };
        label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#59647E"), Color.FromArgb("#A5B1CC"));
        return label;
    }
    public static Border Card(params View[] views) => new() { Content = Stack(14, views) };
    private static VerticalStackLayout Stack(double spacing, IEnumerable<View> views)
    { var stack = new VerticalStackLayout { Spacing = spacing }; foreach (var view in views) stack.Add(view); return stack; }
    public static ScrollView Body(params View[] views)
    { var stack = Stack(24, views); stack.Padding = new Thickness(24, 20, 24, 32); return new() { Content = stack }; }
    public static Label Status(FoundationViewModel model)
    { var label = Muted(""); label.BindingContext = model; label.SetBinding(Label.TextProperty, nameof(model.Status)); return label; }
}

public sealed class TodayPage : ContentPage
{
    private readonly FoundationViewModel model;
    private readonly HabitViewModel habits;
    private readonly DailyViewModel daily;
    private readonly PlanningViewModel planning;
    private readonly WorkoutViewModel workouts;
    private readonly IDashboardStore dashboardStore;
    private readonly VerticalStackLayout habitList = new() { Spacing = 12 };
    private readonly VerticalStackLayout dailyPanel = new() { Spacing = 18 };
    private readonly VerticalStackLayout dashboardHost = new() { Spacing = 18 };
    private IReadOnlyList<DashboardCardSetting> dashboardSettings = [];
    private readonly VerticalStackLayout planningPanel = new() { Spacing = 12 };
    private readonly VerticalStackLayout workoutPanel = new() { Spacing = 12 };
    private readonly Border habitsCard;
    private readonly Border planningCard;
    private readonly Border workoutCard;
    private readonly Label date = PageLayout.Muted("");
    public TodayPage(FoundationViewModel model, HabitViewModel habits, DailyViewModel daily, PlanningViewModel planning, WorkoutViewModel workouts, IDashboardStore dashboardStore)
    {
        this.model = model;
        this.habits = habits;
        this.daily = daily;
        this.planning = planning;
        this.workouts = workouts;
        this.dashboardStore = dashboardStore;
        habitsCard = PageLayout.Card(new Label { Text = "HÁBITOS DE HOJE", FontSize = 12, CharacterSpacing = 2 }, habitList);
        planningCard = PageLayout.Card(new Label { Text = "AGENDA DE HOJE", FontSize = 12, CharacterSpacing = 2 }, planningPanel);
        workoutCard = PageLayout.Card(new Label { Text = "TREINO DE HOJE", FontSize = 12, CharacterSpacing = 2 }, workoutPanel);
        Title = "Hoje";
        var greeting = PageLayout.Heading("");
        greeting.BindingContext = model;
        greeting.SetBinding(Label.TextProperty, nameof(model.Greeting));
        var start = new Button { Text = "Organizar minhas áreas" };
        start.Clicked += async (_, _) => await Shell.Current.GoToAsync("//profile");
        var addHabit = PageLayout.Primary("+ Adicionar hábito");
        addHabit.Clicked += async (_, _) =>
        {
            await habits.LoadAsync();
            await Navigation.PushModalAsync(new NavigationPage(new HabitEditorPage(habits, null, async () =>
            {
                await habits.LoadAsync();
                RenderHabits();
            })));
        };
        var customize = new Button { Text = "Personalizar painel" };
        customize.Clicked += async (_,_) => await Navigation.PushModalAsync(new NavigationPage(new DashboardSettingsPage(dashboardStore, saved => { dashboardSettings = saved; RenderDashboard(); })));
        Content = PageLayout.Body(date, greeting,
            PageLayout.Hero("SEU RITMO • SEU ESPAÇO", "Pequenos passos.\nNovas possibilidades.", "Hábitos, treino e foco conectados ao seu dia."),
            addHabit,
            customize, dashboardHost,
            PageLayout.Card(new Label { Text = "Organize sua rotina", FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted("Dê espaço ao que importa. Organize as áreas da sua vida e acompanhe sua evolução."), start),
            PageLayout.Status(model));
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        date.Text = DateTime.Now.ToString("dddd, dd 'de' MMMM", CultureInfo.GetCultureInfo("pt-BR"));
        await model.LoadAsync();
        await habits.LoadAsync();
        await daily.LoadAsync();
        await planning.LoadAsync();
        await workouts.LoadAsync();
#if ANDROID
        if (planning.Error is null) AndroidReminders.SyncAll(planning);
#endif
        RenderHabits();
        RenderPlanning();
        RenderWorkout();
        RenderDaily();
        dashboardSettings = await dashboardStore.GetDashboardSettingsAsync();
        RenderDashboard();
    }

    private void RenderDashboard()
    {
        dashboardHost.Clear();
        var cards = new Dictionary<string,IView>
        {
            ["habits"] = habitsCard,
            ["planning"] = planningCard,
            ["workout"] = workoutCard,
            ["water"] = dailyPanel,
            ["checkin"] = dailyPanel
        };
        var added = new HashSet<IView>();
        foreach (var setting in dashboardSettings.OrderBy(x=>x.SortOrder)) if (setting.IsVisible && cards.TryGetValue(setting.Key, out var card) && added.Add(card)) dashboardHost.Add(card);
    }

    private void RenderHabits()
    {
        habitList.Clear();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var scheduled = habits.Today(today).ToList();
        if (habits.Error is { } error) { habitList.Add(PageLayout.Muted(error)); return; }
        if (scheduled.Count == 0)
        { habitList.Add(PageLayout.Muted("Nenhum hábito programado para hoje. Dias de descanso não contam como falha.")); return; }
        var done = scheduled.Count(h => h.CompletedOn(today));
        habitList.Add(PageLayout.Muted($"{done} de {scheduled.Count} concluídos"));
        foreach (var habit in scheduled)
        {
            var completed = habit.CompletedOn(today);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
            var label = new Label { Text = habit.RevisionOn(today)?.Name ?? "Hábito", VerticalOptions = LayoutOptions.Center, FontSize = 17 };
            row.Add(label, 0, 0);
            var button = new Button { Text = completed ? "✓ Feito" : "Concluir", BackgroundColor = completed ? Color.FromArgb("#244C51") : Color.FromArgb("#7664E8"), TextColor = Colors.White };
            button.Clicked += async (_, _) =>
            {
                button.IsEnabled = false;
                try { await habits.ToggleTodayAsync(habit, today); RenderHabits(); }
                catch (Exception ex) { await DisplayAlert("Não foi possível salvar", ex.Message, "Entendi"); button.IsEnabled = true; }
            };
            row.Add(button, 1, 0);
            habitList.Add(row);
        }
    }

    private void RenderPlanning()
    {
        planningPanel.Clear();
        if (planning.Error is { } error) { planningPanel.Add(PageLayout.Muted(error)); return; }
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tasks = planning.Tasks.Where(t => !t.IsArchived && t.CompletedAt is null && (t.DueOn is null || t.DueOn <= today)).ToList();
        var events = planning.Appointments.Where(a => !a.IsArchived && DateOnly.FromDateTime(a.StartsAt.ToLocalTime().DateTime) == today).ToList();
        foreach (var item in tasks.Take(5))
        {
            var button = new Button { Text = $"☐ {item.Title}" };
            button.Clicked += async (_, _) => { await planning.CompleteTaskAsync(item);
#if ANDROID
                AndroidReminders.Sync(planning.Tasks.First(t => t.Id == item.Id));
#endif
                RenderPlanning(); };
            planningPanel.Add(button);
        }
        foreach (var item in events) planningPanel.Add(PageLayout.Muted($"{item.StartsAt.ToLocalTime():HH:mm}  {item.Title}"));
        if (tasks.Count == 0 && events.Count == 0) planningPanel.Add(PageLayout.Muted("Sem tarefas ou compromissos para hoje."));
        var open = new Button { Text = "Abrir planejamento" };
        open.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new PlanningPage(planning)));
        planningPanel.Add(open);
    }

    private void RenderWorkout()
    {
        workoutPanel.Clear();
        if (workouts.Error is { } error) { workoutPanel.Add(PageLayout.Muted(error)); return; }
        var today = DateOnly.FromDateTime(DateTime.Today);
        var day = workouts.CurrentPlan?.DayOn(today);
        var completed = workouts.Sessions.Count(s => s.EndedAt is not null &&
            DateOnly.FromDateTime(s.StartedAt.ToLocalTime().DateTime) == today);
        workoutPanel.Add(PageLayout.Muted(workouts.CurrentPlan is null ? "Nenhum plano de treino criado." :
            day is null ? "Descanso programado." : $"{day.Name} • {day.Exercises.Count} exercício(s)"));
        if (completed > 0) workoutPanel.Add(PageLayout.Muted($"{completed} treino(s) concluído(s) hoje"));
        if (workouts.OpenSession is { } open)
        {
            var resume = new Button { Text = "Retomar treino" };
            resume.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WorkoutSessionPage(workouts, open.Id)));
            workoutPanel.Add(resume);
        }
        var openTab = new Button { Text = "Abrir Treino" };
        openTab.Clicked += async (_, _) => await Shell.Current.GoToAsync("//workout");
        workoutPanel.Add(openTab);
    }

    private void RenderDaily()
    {
        dailyPanel.Clear();
        if (daily.Error is { } error) { dailyPanel.Add(PageLayout.Muted(error)); return; }
        var record = daily.Today;
        if (record is null) { dailyPanel.Add(PageLayout.Muted("Check-in indisponível.")); return; }
        var target = record.CheckIn.WaterTargetMl;
        var progress = new ProgressBar { Progress = target is > 0 ? Math.Min(1, (double)record.WaterMl / target.Value) : 0,
            ProgressColor = Color.FromArgb("#88E0B0") };
        var quick = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
        var add250 = new Button { Text = "+250 ml" };
        var add500 = new Button { Text = "+500 ml" };
        add250.Clicked += async (_, _) => await AddWaterAsync(add250, 250);
        add500.Clicked += async (_, _) => await AddWaterAsync(add500, 500);
        quick.Add(add250, 0, 0);
        quick.Add(add500, 1, 0);
        var setGoal = new Button { Text = "Definir meta de hoje" };
        setGoal.Clicked += async (_, _) =>
        {
            var answer = await DisplayPromptAsync("Meta de água", "Quantos ml deseja beber hoje? Deixe vazio para remover a meta.",
                "Salvar", "Cancelar", keyboard: Keyboard.Numeric, maxLength: 5, initialValue: target?.ToString() ?? "");
            if (answer is null) return;
            try
            {
                int? value = null;
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    if (!int.TryParse(answer, out var parsed)) throw new ArgumentException("Informe a meta em ml.");
                    value = parsed;
                }
                await daily.SetTodayWaterTargetAsync(value);
                RenderDaily();
            }
            catch (Exception ex) { await DisplayAlert("Meta inválida", ex.Message, "Entendi"); }
        };
        var correct = new Button { Text = "Corrigir consumo" };
        correct.Clicked += async (_, _) =>
        {
            var answer = await DisplayPromptAsync("Consumo de água", "Qual é o total consumido hoje, em ml?",
                "Salvar", "Cancelar", keyboard: Keyboard.Numeric, maxLength: 8, initialValue: record.WaterMl.ToString());
            if (answer is null) return;
            if (!int.TryParse(answer, out var value)) { await DisplayAlert("Valor inválido", "Informe um número em ml.", "Entendi"); return; }
            try { await daily.SetWaterTotalAsync(value); RenderDaily(); }
            catch (Exception ex) { await DisplayAlert("Não foi possível corrigir", ex.Message, "Entendi"); }
        };
        dailyPanel.Add(PageLayout.Card(new Label { Text = "ÁGUA", FontSize = 12, CharacterSpacing = 2 },
            new Label { Text = target is null ? $"{record.WaterMl} ml hoje • defina uma meta" : $"{record.WaterMl} de {target} ml", FontSize = 21, FontAttributes = FontAttributes.Bold },
            progress, quick, setGoal, correct));

        var checkIn = record.CheckIn;
        var summary = new List<string>();
        if (checkIn.WokeAt is { } wake) summary.Add($"Acordou às {wake:HH:mm}");
        if (checkIn.SleepMinutes is { } sleep) summary.Add($"Sono: {sleep / 60}h {sleep % 60}min");
        if (checkIn.Mood is { } mood) summary.Add($"Humor: {mood}/{checkIn.MoodScaleMax}");
        if (checkIn.PhoneMinutes is { } phone) summary.Add($"Celular: {phone} min");
        if (summary.Count == 0) summary.Add("Check-in opcional ainda não preenchido.");
        var edit = new Button { Text = "Registrar check-in" };
        edit.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new DailyCheckInPage(daily, async () =>
        {
            await daily.LoadAsync();
            RenderDaily();
        })));
        var close = new Button { Text = record.IsClosed ? "Ver fechamento" : "Fechar o dia" };
        close.Clicked += async (_, _) =>
        {
            await habits.LoadAsync();
            await Navigation.PushModalAsync(new NavigationPage(new DailyClosurePage(daily, habits, async () =>
            {
                await daily.LoadAsync();
                RenderDaily();
            })));
        };
        var history = new Button { Text = "Histórico dos dias" };
        history.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new DailyHistoryPage(daily)));
        var details = summary.Select(PageLayout.Muted).Cast<View>().ToList();
        details.Insert(0, new Label { Text = "CHECK-IN", FontSize = 12, CharacterSpacing = 2 });
        if (checkIn.Note is { } note) details.Add(PageLayout.Muted(note));
        if (record.IsClosed) details.Add(PageLayout.Muted("Dia fechado • você pode reabrir ou atualizar o registro"));
        details.Add(edit);
        details.Add(close);
        details.Add(history);
        dailyPanel.Add(PageLayout.Card(details.ToArray()));
        if (dashboardSettings.Count > 0) RenderDashboard();
    }

    private async Task AddWaterAsync(Button button, int amount)
    {
        button.IsEnabled = false;
        try { await daily.AddWaterAsync(amount); RenderDaily(); }
        catch (Exception ex) { await DisplayAlert("Não foi possível registrar água", ex.Message, "Entendi"); button.IsEnabled = true; }
    }
}

public sealed class OverviewPage : ContentPage
{
    public OverviewPage(string title, string heading, string description, string note)
    {
        Title = title;
        Content = PageLayout.Body(PageLayout.Muted("SEU ESPAÇO PESSOAL"), PageLayout.Heading(heading),
            PageLayout.Card(new Label { Text = description, FontSize = 20 }, PageLayout.Muted(note)));
    }
}

public sealed class ProfilePage : ContentPage
{
    private readonly FoundationViewModel model;
    private readonly DailyViewModel daily;
    private readonly IBackupService backup;
    private readonly IJournalStore journal;
    public ProfilePage(FoundationViewModel model, DailyViewModel daily, IBackupService backup, IJournalStore journal)
    {
        this.model = model;
        this.daily = daily;
        this.backup = backup;
        this.journal = journal;
        Title = "Perfil";
        BindingContext = model;
        var name = new Entry { Placeholder = "Como você quer ser chamado?", MaxLength = 60 };
        name.SetBinding(Entry.TextProperty, nameof(model.Name));
        var theme = new Picker { Title = "Aparência", ItemsSource = new[] { "Escuro", "Claro", "Usar tema do sistema" } };
        theme.SetBinding(Picker.SelectedIndexProperty, nameof(model.Theme));
        var save = PageLayout.Primary("Salvar preferências");
        save.SetBinding(IsEnabledProperty, nameof(model.Ready));
        save.Clicked += async (_, _) => await model.SaveProfileAsync();
        var add = new Button { Text = "+ Nova área da vida" };
        add.SetBinding(IsEnabledProperty, nameof(model.Ready));
        add.Clicked += async (_, _) => await EditAreaAsync(null);
        var areas = new VerticalStackLayout { Spacing = 12 };
        BindableLayout.SetItemsSource(areas, model.Areas);
        BindableLayout.SetEmptyViewTemplate(areas, new DataTemplate(() => PageLayout.Muted("Nenhuma área criada. Saúde, Estudos e Pessoal são algumas ideias.")));
        BindableLayout.SetItemTemplate(areas, new DataTemplate(() =>
        {
            var title = new Label { FontAttributes = FontAttributes.Bold, FontSize = 18 };
            title.SetBinding(Label.TextProperty, nameof(LifeArea.Name));
            var state = PageLayout.Muted("");
            state.SetBinding(Label.TextProperty, new Binding(nameof(LifeArea.IsArchived), converter: new ArchiveLabelConverter()));
            var edit = new Button { Text = "Editar área" };
            edit.Clicked += async (_, _) => { if (edit.BindingContext is LifeArea area) await EditAreaAsync(area); };
            var swatch = new BoxView { WidthRequest = 24, HeightRequest = 6, HorizontalOptions = LayoutOptions.Start };
            swatch.SetBinding(BoxView.ColorProperty, nameof(LifeArea.ColorHex));
            return PageLayout.Card(swatch, title, state, edit);
        }));
        var busy = new ActivityIndicator();
        busy.SetBinding(ActivityIndicator.IsRunningProperty, nameof(model.Busy));
        busy.SetBinding(IsVisibleProperty, nameof(model.Busy));
        var dailySettings = new Button { Text = "Padrões do check-in" };
        dailySettings.Clicked += async (_, _) =>
        {
            await daily.LoadAsync();
            await Navigation.PushModalAsync(new NavigationPage(new DailySettingsPage(daily)));
        };
        var export = new Button { Text = "Exportar dados (JSON)" };
        export.Clicked += async (_,_) => { try { var path=await backup.ExportJsonAsync(); await Share.Default.RequestAsync(new ShareFileRequest { Title="Exportar dados da Rotina", File=new ShareFile(path) }); } catch(Exception ex) { await DisplayAlert("Exportação", ex.Message, "Entendi"); } };
        var localBackup = new Button { Text = "Criar backup local" };
        localBackup.Clicked += async (_,_) => { try { await backup.CreateLocalBackupAsync(); await DisplayAlert("Backup", "Backup local criado neste dispositivo.", "Entendi"); } catch(Exception ex) { await DisplayAlert("Backup", ex.Message, "Entendi"); } };
        var restore = new Button { Text = "Restaurar último backup local" };
        restore.Clicked += async (_,_) => { var files=await backup.ListLocalBackupsAsync(); if(files.Count==0){await DisplayAlert("Restauração","Nenhum backup local encontrado.","Entendi");return;} if(!await DisplayAlert("Restaurar backup?","Os dados atuais serão substituídos. Reinicie o app depois.","Restaurar","Cancelar"))return; await backup.RestoreLocalBackupAsync(files[0]); await DisplayAlert("Restauração concluída","Feche e abra o app para carregar os dados restaurados.","Entendi"); };
        var journalButton = new Button { Text = "Abrir diário" }; journalButton.Clicked += async (_,_) => await Navigation.PushModalAsync(new NavigationPage(new JournalPage(journal)));
        Content = PageLayout.Body(PageLayout.Heading("Do seu jeito."),
            PageLayout.Card(new Label { Text = "Seu nome" }, name, new Label { Text = "Tema" }, theme, save),
            PageLayout.Status(model), busy,
            PageLayout.Card(new Label { Text = "Check-in diário", FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted("Escolha a meta padrão de água e a escala de humor."), dailySettings),
            PageLayout.Card(new Label { Text = "Backup e privacidade", FontSize = 20, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted("Seus dados permanecem neste dispositivo até você exportá-los ou compartilhá-los."), export, localBackup, restore),
            journalButton,
            new Label { Text = "Áreas da vida", FontSize = 23, FontAttributes = FontAttributes.Bold }, add, areas,
            PageLayout.Muted("Dados salvos somente neste dispositivo. Backup e restauração serão adicionados antes da versão de uso diário."));
    }
    protected override async void OnAppearing() { base.OnAppearing(); await model.LoadAsync(); }
    private async Task EditAreaAsync(LifeArea? area)
    {
        if (model.Busy) return;
        if (area is not null)
        {
            var action = await DisplayActionSheet(area.Name, "Cancelar", null, "Editar", area.IsArchived ? "Restaurar área" : "Arquivar área");
            if (action == "Cancelar" || action is null) return;
            if (action != "Editar")
            {
                if (area.IsArchived || await DisplayAlert("Arquivar área?", "A área e seus vínculos serão preservados. Você poderá restaurá-la.", "Arquivar", "Cancelar"))
                    await model.SaveAreaAsync(area.Id, area.Name, area.ColorHex, area.SortOrder, !area.IsArchived);
                return;
            }
        }
        var name = await DisplayPromptAsync("Área da vida", "Qual é o nome da área?", "Continuar", "Cancelar", maxLength: 60, initialValue: area?.Name ?? "");
        if (name is null) return;
        var color = await DisplayPromptAsync("Cor da área", "Use uma cor hexadecimal, por exemplo #88E0B0.", "Continuar", "Cancelar", maxLength: 7, initialValue: area?.ColorHex ?? "#88E0B0");
        if (color is null) return;
        var order = await DisplayPromptAsync("Ordem", "Posição na lista, a partir de 0.", "Salvar", "Cancelar", keyboard: Keyboard.Numeric, initialValue: (area?.SortOrder ?? model.Areas.Count).ToString());
        if (order is null) return;
        if (!int.TryParse(order, out var position) || position < 0)
        { await DisplayAlert("Posição inválida", "Use um número inteiro a partir de zero.", "Entendi"); return; }
        await model.SaveAreaAsync(area?.Id ?? Guid.NewGuid(), name, color, position, area?.IsArchived ?? false);
    }
}
internal sealed class ArchiveLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? "Arquivada • histórico preservado" : "Ativa";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
