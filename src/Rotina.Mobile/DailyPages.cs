using System.Globalization;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class DailyCheckInPage : ContentPage
{
    private readonly DailyViewModel model;
    private readonly Func<Task> saved;
    private readonly Switch hasWake = new();
    private readonly TimePicker wake = new() { Format = "HH:mm", Time = new TimeSpan(7, 0, 0) };
    private readonly Entry sleepHours = new() { Placeholder = "Horas", Keyboard = Keyboard.Numeric, MaxLength = 2 };
    private readonly Entry sleepMinutes = new() { Placeholder = "Minutos", Keyboard = Keyboard.Numeric, MaxLength = 2 };
    private readonly Picker sleepRating = new() { Title = "Opcional", ItemsSource = new[] { "1", "2", "3", "4", "5" } };
    private readonly Picker mood = new() { Title = "Opcional" };
    private readonly Entry phoneMinutes = new() { Placeholder = "Minutos de celular", Keyboard = Keyboard.Numeric, MaxLength = 4 };
    private readonly Entry waterTarget = new() { Placeholder = "Meta de água em ml", Keyboard = Keyboard.Numeric, MaxLength = 5 };
    private readonly Editor note = new() { Placeholder = "Como foi seu dia?", MaxLength = 500, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 90 };
    private readonly Label error = PageLayout.Muted("");
    private readonly Button save = new() { Text = "Salvar check-in" };

    public DailyCheckInPage(DailyViewModel model, Func<Task> saved)
    {
        this.model = model;
        this.saved = saved;
        Title = "Check-in diário";
        var current = model.Today?.CheckIn ?? new DailyCheckIn(model.LocalToday, moodScaleMax: model.Preferences.MoodScaleMax);
        hasWake.IsToggled = current.WokeAt is not null;
        if (current.WokeAt is not null) wake.Time = current.WokeAt.Value.ToTimeSpan();
        wake.IsEnabled = hasWake.IsToggled;
        hasWake.Toggled += (_, args) => wake.IsEnabled = args.Value;
        if (current.SleepMinutes is { } sleep)
        { sleepHours.Text = (sleep / 60).ToString(); sleepMinutes.Text = (sleep % 60).ToString(); }
        sleepRating.SelectedIndex = current.SleepRating is null ? -1 : current.SleepRating.Value - 1;
        for (var i = 1; i <= current.MoodScaleMax; i++) mood.Items.Add(i.ToString());
        mood.SelectedIndex = current.Mood is null ? -1 : current.Mood.Value - 1;
        phoneMinutes.Text = current.PhoneMinutes?.ToString() ?? "";
        waterTarget.Text = current.WaterTargetMl?.ToString() ?? "";
        note.Text = current.Note ?? "";
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        save.Clicked += OnSave;
        Content = PageLayout.Body(PageLayout.Heading("Como foi hoje?"),
            PageLayout.Muted("Preencha apenas o que fizer sentido. Você pode voltar e ajustar depois."),
            PageLayout.Card(new Label { Text = "Acordei às" },
                new HorizontalStackLayout { Spacing = 12, Children = { hasWake, new Label { Text = "Registrar horário", VerticalOptions = LayoutOptions.Center } } }, wake,
                new Label { Text = "Sono" },
                new HorizontalStackLayout { Spacing = 12, Children = { sleepHours, sleepMinutes } },
                new Label { Text = "Como dormi? (1 a 5)" }, sleepRating,
                new Label { Text = $"Humor (1 a {current.MoodScaleMax})" }, mood,
                new Label { Text = "Tempo de celular (manual)" }, phoneMinutes,
                new Label { Text = "Meta de água de hoje" }, waterTarget,
                new Label { Text = "Nota do dia" }, note, error, save));
    }

    private async void OnSave(object? sender, EventArgs e)
    {
        save.IsEnabled = false;
        try
        {
            int? sleep = null;
            if (!string.IsNullOrWhiteSpace(sleepHours.Text) || !string.IsNullOrWhiteSpace(sleepMinutes.Text))
            {
                if (!int.TryParse(sleepHours.Text is { Length: > 0 } ? sleepHours.Text : "0", out var hours) ||
                    !int.TryParse(sleepMinutes.Text is { Length: > 0 } ? sleepMinutes.Text : "0", out var minutes) ||
                    hours < 0 || minutes is < 0 or > 59)
                    throw new ArgumentException("Informe horas e minutos de sono válidos.");
                sleep = checked(hours * 60 + minutes);
            }
            var checkIn = new DailyCheckIn(model.LocalToday,
                hasWake.IsToggled ? TimeOnly.FromTimeSpan(wake.Time) : null,
                sleep, sleepRating.SelectedIndex < 0 ? null : sleepRating.SelectedIndex + 1,
                mood.SelectedIndex < 0 ? null : mood.SelectedIndex + 1,
                ParseOptional(phoneMinutes.Text, "tempo de celular"), note.Text,
                ParseOptional(waterTarget.Text, "meta de água"), mood.Items.Count);
            await model.SaveCheckInAsync(checkIn);
            await Navigation.PopModalAsync();
            await saved();
        }
        catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
    }

    private static int? ParseOptional(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!int.TryParse(text, out var value)) throw new ArgumentException($"Informe um valor numérico para {label}.");
        return value;
    }
}

public sealed class DailyClosurePage : ContentPage
{
    private readonly DailyViewModel model;
    private readonly HabitViewModel habits;
    private readonly Func<Task> saved;
    private readonly Picker rating = new() { Title = "Opcional", ItemsSource = new[] { "1", "2", "3", "4", "5" } };
    private readonly Editor note = new() { Placeholder = "Algo importante sobre o dia?", MaxLength = 500, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 90 };
    private readonly Button close = new() { Text = "Fechar o dia" };
    private readonly Label error = PageLayout.Muted("");

    public DailyClosurePage(DailyViewModel model, HabitViewModel habits, Func<Task> saved)
    {
        this.model = model;
        this.habits = habits;
        this.saved = saved;
        Title = "Fechar o dia";
        var today = model.LocalToday;
        var record = model.Today ?? new DailyRecord(new DailyCheckIn(today), 0, [], []);
        var scheduled = habits.Today(today).ToList();
        var done = scheduled.Count(h => h.CompletedOn(today));
        var target = record.CheckIn.WaterTargetMl;
        var prior = record.LatestClosure;
        rating.SelectedIndex = prior?.OverallRating is { } value ? value - 1 : -1;
        note.Text = prior?.Note ?? "";
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        close.Clicked += OnClose;
        var reopen = new Button { Text = "Reabrir o dia", IsVisible = record.IsClosed };
        reopen.Clicked += async (_, _) =>
        {
            if (!await DisplayAlert("Reabrir o dia?", "O fechamento anterior ficará no histórico.", "Reabrir", "Cancelar")) return;
            try { await model.CloseAsync(false, null, null); await Navigation.PopModalAsync(); await saved(); }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        Content = PageLayout.Body(PageLayout.Heading("Fechamento do dia"),
            PageLayout.Muted("Um retrato do que você registrou hoje."),
            PageLayout.Card(new Label { Text = "Resumo", FontSize = 21, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"Hábitos: {done} de {scheduled.Count} concluídos"),
                PageLayout.Muted(target is null ? $"Água: {record.WaterMl} ml" : $"Água: {record.WaterMl} de {target} ml"),
                PageLayout.Muted($"Sono: {FormatSleep(record.CheckIn.SleepMinutes)}"),
                PageLayout.Muted(record.CheckIn.Mood is null ? "Humor: sem registro" : $"Humor: {record.CheckIn.Mood}/{record.CheckIn.MoodScaleMax}"),
                PageLayout.Muted($"Treinos: {model.WorkoutsToday} concluído(s)"),
                PageLayout.Muted($"Foco: {model.FocusMinutesToday} minuto(s)")),
            PageLayout.Card(new Label { Text = "Avaliação geral (1 a 5)" }, rating,
                new Label { Text = "Nota opcional" }, note, error, close, reopen));
    }

    private async void OnClose(object? sender, EventArgs e)
    {
        close.IsEnabled = false;
        try
        {
            await model.CloseAsync(true, rating.SelectedIndex < 0 ? null : rating.SelectedIndex + 1, note.Text);
            await Navigation.PopModalAsync();
            await saved();
        }
        catch (Exception ex) { error.Text = ex.Message; close.IsEnabled = true; }
    }

    private static string FormatSleep(int? minutes) => minutes is null ? "sem registro" : $"{minutes / 60}h {minutes % 60}min";
}

public sealed class DailyHistoryPage : ContentPage
{
    private readonly DailyViewModel model;
    private readonly VerticalStackLayout list = new() { Spacing = 14 };
    private readonly DatePicker from = new() { Format = "dd/MM/yyyy" };
    private readonly DatePicker through = new() { Format = "dd/MM/yyyy" };

    public DailyHistoryPage(DailyViewModel model)
    {
        this.model = model;
        Title = "Histórico diário";
        from.Date = DateTime.Today.AddDays(-29);
        through.Date = DateTime.Today;
        through.MaximumDate = DateTime.Today;
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        var search = new Button { Text = "Mostrar período" };
        search.Clicked += async (_, _) => await LoadAsync();
        Content = PageLayout.Body(PageLayout.Heading("Seus dias"),
            PageLayout.Muted("Os registros anteriores continuam disponíveis quando um novo dia começa."),
            PageLayout.Card(new Label { Text = "De" }, from, new Label { Text = "Até" }, through, search), list);
    }

    protected override async void OnAppearing() { base.OnAppearing(); await LoadAsync(); }

    private async Task LoadAsync()
    {
        list.Clear();
        try
        {
            var records = await model.HistoryAsync(DateOnly.FromDateTime(from.Date), DateOnly.FromDateTime(through.Date));
            if (records.Count == 0) { list.Add(PageLayout.Muted("Ainda não há registros neste período.")); return; }
            foreach (var record in records)
            {
                var day = record.CheckIn.Date;
                var info = new List<View>
                {
                    new Label { Text = day.ToString("dddd, dd 'de' MMMM", CultureInfo.GetCultureInfo("pt-BR")), FontSize = 18, FontAttributes = FontAttributes.Bold },
                    PageLayout.Muted(record.IsClosed ? "Dia fechado" : "Registro em andamento"),
                    PageLayout.Muted($"Água: {record.WaterMl} ml"),
                    PageLayout.Muted(record.CheckIn.Mood is null ? "Humor: sem registro" : $"Humor: {record.CheckIn.Mood}/{record.CheckIn.MoodScaleMax}")
                };
                if (record.CheckIn.WokeAt is { } woke) info.Add(PageLayout.Muted($"Acordou: {woke:HH:mm}"));
                if (record.CheckIn.SleepMinutes is { } sleep) info.Add(PageLayout.Muted($"Sono: {sleep / 60}h {sleep % 60}min"));
                if (record.CheckIn.Note is { } note) info.Add(PageLayout.Muted(note));
                list.Add(PageLayout.Card(info.ToArray()));
            }
        }
        catch (Exception ex) { list.Add(PageLayout.Muted(ex.Message)); }
    }
}

public sealed class DailySettingsPage : ContentPage
{
    private readonly DailyViewModel model;
    private readonly Entry waterTarget = new() { Placeholder = "Sem meta padrão", Keyboard = Keyboard.Numeric, MaxLength = 5 };
    private readonly Picker moodScale = new() { Title = "Escala de humor", ItemsSource = new[] { "3 pontos", "5 pontos", "10 pontos" } };
    private readonly Label error = PageLayout.Muted("");
    private readonly Button save = new() { Text = "Salvar padrão" };

    public DailySettingsPage(DailyViewModel model)
    {
        this.model = model;
        Title = "Preferências do dia";
        waterTarget.Text = model.Preferences.DefaultWaterTargetMl?.ToString() ?? "";
        moodScale.SelectedIndex = model.Preferences.MoodScaleMax switch { 3 => 0, 10 => 2, _ => 1 };
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                int? target = null;
                if (!string.IsNullOrWhiteSpace(waterTarget.Text))
                {
                    if (!int.TryParse(waterTarget.Text, out var value)) throw new ArgumentException("Informe a meta de água em ml.");
                    target = value;
                }
                await model.SavePreferencesAsync(new DailyPreferences(target, moodScale.SelectedIndex switch { 0 => 3, 2 => 10, _ => 5 }));
                await Navigation.PopModalAsync();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading("Padrões do check-in"),
            PageLayout.Muted("As mudanças valem para novos dias. Registros anteriores mantêm suas metas e escalas."),
            PageLayout.Card(new Label { Text = "Meta diária de água (ml)" }, waterTarget,
                new Label { Text = "Escala de humor" }, moodScale, error, save));
    }
}
