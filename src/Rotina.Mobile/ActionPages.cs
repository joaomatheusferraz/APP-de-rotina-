using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class ActionBoardPage : ContentPage
{
    private readonly GoalViewModel model;
    private readonly VerticalStackLayout columns = new() { Spacing = 20 };
    private readonly Switch showArchived = new();
    public ActionBoardPage(GoalViewModel model)
    {
        this.model = model;
        Title = "Plano de Ação";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        var add = new Button { Text = "+ Novo cartão" };
        add.Clicked += async (_, _) => await EditAsync(null);
        showArchived.Toggled += (_, _) => Render();
        Content = PageLayout.Body(PageLayout.Heading("Plano de Ação"),
            PageLayout.Muted("Mova cartões entre as etapas. Cada mudança fica no histórico."), add,
            new HorizontalStackLayout { Spacing = 10, Children = { showArchived,
                new Label { Text = "Mostrar arquivados", VerticalOptions = LayoutOptions.Center } } }, columns);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await RefreshAsync(); }
    private async Task RefreshAsync() { await model.LoadAsync(); Render(); }
    private void Render()
    {
        columns.Clear();
        if (model.Error is { } error) { columns.Add(PageLayout.Muted(error)); return; }
        foreach (var lane in model.Lanes)
        {
            var items = model.Cards.Where(c => c.LaneId == lane.Id && (showArchived.IsToggled || !c.IsArchived)).ToList();
            var stack = new VerticalStackLayout { Spacing = 14 };
            stack.Add(new Label { Text = $"{lane.Title} • {items.Count}", FontSize = 23, FontAttributes = FontAttributes.Bold });
            if (items.Count == 0) stack.Add(PageLayout.Muted("Nenhum cartão nesta etapa."));
            foreach (var card in items) stack.Add(CardView(card));
            columns.Add(stack);
        }
    }
    private View CardView(ActionCard card)
    {
        var title = new Label { Text = card.Title, FontSize = 19, FontAttributes = FontAttributes.Bold };
        var details = new List<string>();
        if (card.DueOn is { } due) details.Add($"Prazo {due:dd/MM/yyyy}");
        if (card.Tags is { } tags) details.Add(tags);
        if (card.GoalId is { } goalId) details.Add($"Meta: {model.Goals.FirstOrDefault(g => g.Id == goalId)?.Title ?? "vinculada"}");
        if (card.IsArchived) details.Add("Arquivado");
        var move = new Button { Text = "Mover cartão" };
        move.Clicked += async (_, _) =>
        {
            var choices = model.Lanes.Where(l => l.Id != card.LaneId).Select(l => $"{l.Title} ({l.Id})").ToArray();
            var choice = await DisplayActionSheet("Mover para", "Cancelar", null, choices);
            var lane = model.Lanes.FirstOrDefault(l => choice == $"{l.Title} ({l.Id})");
            if (lane is null) return;
            try { await model.MoveCardAsync(card, lane.Id); Render(); }
            catch (Exception ex) { await DisplayAlert("Não foi possível mover", ex.Message, "Entendi"); }
        };
        var edit = new Button { Text = "Editar" };
        edit.Clicked += async (_, _) => await EditAsync(card);
        var history = new Button { Text = "Histórico de etapas" };
        history.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new ActionHistoryPage(model, card.Id)));
        var archive = new Button { Text = card.IsArchived ? "Restaurar" : "Arquivar" };
        archive.Clicked += async (_, _) =>
        {
            if (!card.IsArchived && !await DisplayAlert("Arquivar cartão?", "O histórico ficará disponível.", "Arquivar", "Cancelar")) return;
            await model.ArchiveCardAsync(card, !card.IsArchived);
            Render();
        };
        return PageLayout.Card(title, PageLayout.Muted(card.Description ?? ""), PageLayout.Muted(string.Join(" • ", details)),
            move, edit, history, archive);
    }
    private async Task EditAsync(ActionCard? card) =>
        await Navigation.PushModalAsync(new NavigationPage(new ActionEditorPage(model, card, RefreshAsync)));
}

public sealed class ActionEditorPage : ContentPage
{
    private readonly GoalViewModel model;
    private readonly ActionCard? existing;
    private readonly Func<Task> saved;
    private readonly Entry title = new() { Placeholder = "Ex.: Organizar estudos", MaxLength = 120 };
    private readonly Editor description = new() { Placeholder = "Descrição opcional", HeightRequest = 100, MaxLength = 2000 };
    private readonly Entry tags = new() { Placeholder = "Ex.: saúde, rotina", MaxLength = 300 };
    private readonly Picker lane = new() { Title = "Etapa" };
    private readonly Picker goal = new() { Title = "Meta vinculada" };
    private readonly Picker area;
    private readonly Switch hasDue = new();
    private readonly DatePicker due = new() { Format = "dd/MM/yyyy" };
    private readonly Label error = PageLayout.Muted("");
    public ActionEditorPage(GoalViewModel model, ActionCard? existing, Func<Task> saved)
    {
        this.model = model; this.existing = existing; this.saved = saved;
        Title = existing is null ? "Novo cartão" : "Editar cartão";
        title.Text = existing?.Title ?? "";
        description.Text = existing?.Description ?? "";
        tags.Text = existing?.Tags ?? "";
        foreach (var item in model.Lanes) lane.Items.Add(item.Title);
        lane.SelectedIndex = Math.Max(0, model.Lanes.ToList().FindIndex(l => l.Id == existing?.LaneId));
        var goals = model.Goals.Where(g => !g.IsArchived || g.Id == existing?.GoalId).ToList();
        goal.Items.Add("Sem meta");
        foreach (var item in goals) goal.Items.Add(item.Title);
        goal.SelectedIndex = existing?.GoalId is { } goalId ? Math.Max(0, 1 + goals.FindIndex(g => g.Id == goalId)) : 0;
        var areas = model.Areas.Where(a => !a.IsArchived || a.Id == existing?.AreaId).ToList();
        area = PlanningUi.AreaPicker(areas, existing?.AreaId);
        hasDue.IsToggled = existing?.DueOn is not null;
        due.Date = existing?.DueOn?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today.AddDays(7);
        due.IsVisible = hasDue.IsToggled;
        hasDue.Toggled += (_, _) => due.IsVisible = hasDue.IsToggled;
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        var save = new Button { Text = "Salvar cartão" };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                if (lane.SelectedIndex < 0 || lane.SelectedIndex >= model.Lanes.Count) throw new ArgumentException("Escolha uma etapa.");
                var currentGoals = model.Goals.Where(g => !g.IsArchived || g.Id == existing?.GoalId).ToList();
                var currentAreas = model.Areas.Where(a => !a.IsArchived || a.Id == existing?.AreaId).ToList();
                var card = new ActionCard(existing?.Id ?? Guid.NewGuid(), title.Text ?? "", description.Text, tags.Text,
                    PlanningUi.SelectedArea(area, currentAreas), hasDue.IsToggled ? DateOnly.FromDateTime(due.Date) : null,
                    goal.SelectedIndex > 0 && goal.SelectedIndex <= currentGoals.Count ? currentGoals[goal.SelectedIndex - 1].Id : null,
                    model.Lanes[lane.SelectedIndex].Id, existing?.IsArchived ?? false);
                await model.SaveCardAsync(card);
                await Navigation.PopModalAsync();
                await saved();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading(Title),
            PageLayout.Card(new Label { Text = "Título" }, title, new Label { Text = "Descrição" }, description,
                new Label { Text = "Tags" }, tags, new Label { Text = "Etapa" }, lane,
                new Label { Text = "Área" }, area, new Label { Text = "Meta" }, goal,
                new HorizontalStackLayout { Spacing = 10, Children = { hasDue, new Label { Text = "Definir prazo", VerticalOptions = LayoutOptions.Center } } },
                due, error, save));
    }
}

public sealed class ActionHistoryPage : ContentPage
{
    private readonly GoalViewModel model;
    private readonly Guid cardId;
    private readonly VerticalStackLayout content = new() { Spacing = 14 };
    public ActionHistoryPage(GoalViewModel model, Guid cardId)
    {
        this.model = model; this.cardId = cardId;
        Title = "Histórico do cartão";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        Content = new ScrollView { Content = content };
        content.Padding = new Thickness(24, 20, 24, 32);
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        content.Clear();
        var card = model.Cards.FirstOrDefault(c => c.Id == cardId);
        content.Add(PageLayout.Heading(card?.Title ?? "Cartão"));
        foreach (var move in await model.MovesAsync(cardId))
            content.Add(PageLayout.Card(new Label { Text = model.Lanes.FirstOrDefault(l => l.Id == move.LaneId)?.Title ?? move.LaneId,
                FontSize = 19 }, PageLayout.Muted(move.RecordedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))));
    }
}
