using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class FocusPage : ContentPage
{
    private readonly FocusViewModel model;
    private readonly VerticalStackLayout content = new() { Spacing = 18, Padding = new Thickness(24, 20, 24, 32) };
    private readonly Picker period = new() { Title = "Período", ItemsSource = new[] { "7 dias", "30 dias", "90 dias" }, SelectedIndex = 1 };
    public FocusPage(FocusViewModel model)
    {
        this.model = model; Title = "Foco";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync(); ToolbarItems.Add(close);
        period.SelectedIndexChanged += (_, _) => Render();
        Content = new ScrollView { Content = content };
    }
    protected override async void OnAppearing() { base.OnAppearing(); await model.LoadAsync(); Render(); }
    private void Render()
    {
        content.Clear(); content.Add(PageLayout.Heading("Modo Foco"));
        content.Add(PageLayout.Muted("Registre sessões de estudo ou trabalho com duração real e categoria."));
        if (model.Error is { } error) { content.Add(PageLayout.Muted(error)); return; }
        if (model.Open is { } open)
        {
            var timer = new FocusTimerPage(model, open.Id);
            var resume = new Button { Text = open.Status == FocusStatus.Paused ? "Retomar foco" : "Abrir foco em andamento" };
            resume.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(timer));
            content.Add(PageLayout.Card(new Label { Text = open.Title, FontSize = 21, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"{open.Category} • alvo {open.TargetMinutes} min"), resume));
        }
        else
        {
            var start = new Button { Text = "+ Iniciar sessão de foco" };
            start.Clicked += async (_, _) => await StartAsync();
            content.Add(start);
        }
        var days = new[] { 7, 30, 90 }[Math.Max(0, period.SelectedIndex)];
        var stats = FocusAnalytics.ByCategory(model.Sessions, DateOnly.FromDateTime(DateTime.Today), days);
        content.Add(PageLayout.Card(new Label { Text = "Estatísticas", FontSize = 21, FontAttributes = FontAttributes.Bold },
            new Label { Text = "Período" }, period,
            PageLayout.Muted(stats.Count == 0 ? "Nenhuma sessão encerrada neste período." :
                $"{stats.Sum(s => s.Sessions)} sessões • {stats.Sum(s => s.Minutes)} minutos")));
        foreach (var stat in stats)
            content.Add(PageLayout.Card(new Label { Text = stat.Category, FontSize = 18, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"{stat.Sessions} sessão(ões) • {stat.Minutes} minutos")));
        content.Add(new Label { Text = "Histórico", FontSize = 21, FontAttributes = FontAttributes.Bold });
        foreach (var session in model.Sessions.Where(s => s.EndedAt is not null).Reverse().Take(20))
            content.Add(PageLayout.Card(new Label { Text = session.Title, FontSize = 18, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"{session.Category} • {session.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm} • {session.ElapsedMinutes(session.EndedAt!.Value)} min"),
                PageLayout.Muted(session.Rating is { } rating ? $"Avaliação: {rating}/5" : "Sem avaliação"),
                PageLayout.Muted(session.Note ?? "")));
    }
    private async Task StartAsync()
    {
        var title = await DisplayPromptAsync("Sessão de foco", "Nome da sessão", "Continuar", "Cancelar", maxLength: 120);
        if (title is null) return;
        var category = await DisplayPromptAsync("Categoria", "Ex.: Faculdade, C#, POO", "Continuar", "Cancelar", maxLength: 80);
        if (category is null) return;
        var target = await DisplayPromptAsync("Duração alvo", "Minutos", "Iniciar", "Cancelar", keyboard: Keyboard.Numeric, initialValue: "25");
        if (target is null) return;
        try
        {
            if (!int.TryParse(target, out var minutes)) throw new ArgumentException("Informe a duração em minutos.");
            await model.StartAsync(title, category, minutes); Render();
        }
        catch (Exception ex) { await DisplayAlert("Sessão inválida", ex.Message, "Entendi"); }
    }
}

public sealed class FocusTimerPage : ContentPage
{
    private readonly FocusViewModel model;
    private readonly Guid sessionId;
    private readonly Label elapsed = new() { FontSize = 48, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label state = PageLayout.Muted("");
    public FocusTimerPage(FocusViewModel model, Guid sessionId)
    {
        this.model = model; this.sessionId = sessionId; Title = "Foco";
        var close = new ToolbarItem { Text = "Fechar" }; close.Clicked += async (_, _) => await Navigation.PopModalAsync(); ToolbarItems.Add(close);
        Content = PageLayout.Body(PageLayout.Heading("Sessão em foco"), elapsed, state);
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); await model.LoadAsync(); Render();
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () => { Render(); return model.Open?.Id == sessionId; });
    }
    private void Render()
    {
        var session = model.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null) return;
        elapsed.Text = $"{session.ElapsedMinutes(DateTimeOffset.UtcNow) / 60:00}:{session.ElapsedMinutes(DateTimeOffset.UtcNow) % 60:00}";
        state.Text = $"{session.Title} • {session.Category} • alvo {session.TargetMinutes} min • {session.Status}";
        var stack = (VerticalStackLayout)((ScrollView)Content).Content;
        while (stack.Children.Count > 3) stack.RemoveAt(stack.Children.Count - 1);
        if (session.Status == FocusStatus.Paused)
        {
            var resume = new Button { Text = "Retomar" }; resume.Clicked += async (_, _) => { await model.ResumeAsync(session); Render(); }; stack.Add(resume);
        }
        else
        {
            var pause = new Button { Text = "Pausar" }; pause.Clicked += async (_, _) => { await model.PauseAsync(session); Render(); }; stack.Add(pause);
        }
        var finish = new Button { Text = "Encerrar sessão" }; finish.Clicked += async (_, _) => await FinishAsync(session); stack.Add(finish);
    }
    private async Task FinishAsync(FocusSession session)
    {
        var answer = await DisplayPromptAsync("Avaliação", "De 1 a 5 (opcional)", "Continuar", "Cancelar", keyboard: Keyboard.Numeric);
        if (answer is null) return;
        int? rating = string.IsNullOrWhiteSpace(answer) ? null : int.TryParse(answer, out var value) ? value : null;
        var note = await DisplayPromptAsync("Nota", "Como foi?", "Salvar", "Cancelar", maxLength: 1000);
        if (note is null) return;
        try { await model.FinishAsync(session, rating, note); await Navigation.PopModalAsync(); }
        catch (Exception ex) { await DisplayAlert("Não foi possível encerrar", ex.Message, "Entendi"); }
    }
}
