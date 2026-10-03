using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class DreamBoardPage : ContentPage
{
    private readonly IDreamBoardStore store;
    private readonly GoalViewModel goals;
    private readonly VerticalStackLayout list = new() { Spacing = 16 };
    private IReadOnlyList<DreamBoardItem> items = [];
    public DreamBoardPage(IDreamBoardStore store, GoalViewModel goals)
    {
        this.store = store; this.goals = goals; Title = "Quadro dos Sonhos";
        var close = new ToolbarItem { Text = "Fechar" }; close.Clicked += async (_, _) => await Navigation.PopModalAsync(); ToolbarItems.Add(close);
        var add = new Button { Text = "+ Adicionar imagem da galeria" }; add.Clicked += async (_, _) => await AddAsync();
        Content = PageLayout.Body(PageLayout.Heading("Quadro dos Sonhos"),
            PageLayout.Muted("Imagens ficam copiadas no armazenamento privado do app. Vínculos com metas e cartões são opcionais."), add, list);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await RefreshAsync(); }
    private async Task RefreshAsync()
    {
        items = await store.GetDreamBoardItemsAsync(); list.Clear();
        var active = items.Where(i => !i.IsArchived).ToList();
        if (active.Count == 0) { list.Add(PageLayout.Muted("Seu mural ainda está vazio.")); return; }
        foreach (var item in active)
        {
            var image = new Image { Source = File.Exists(item.ImagePath) ? ImageSource.FromFile(item.ImagePath) : null,
                HeightRequest = item.Size switch { DreamCardSize.Large => 250, DreamCardSize.Medium => 180, _ => 120 }, Aspect = Aspect.AspectFill };
            var open = new TapGestureRecognizer(); open.Tapped += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new DreamImagePage(item)));
            image.GestureRecognizers.Add(open);
            var edit = new Button { Text = "Editar cartão" }; edit.Clicked += async (_, _) => await EditAsync(item);
            var up = new Button { Text = "↑" }; up.IsEnabled = item.SortOrder > 0; up.Clicked += async (_, _) => { await store.MoveDreamBoardItemAsync(item.Id, Math.Max(0, item.SortOrder - 1)); await RefreshAsync(); };
            var down = new Button { Text = "↓" }; down.IsEnabled = item.SortOrder < active.Count - 1; down.Clicked += async (_, _) => { await store.MoveDreamBoardItemAsync(item.Id, item.SortOrder + 1); await RefreshAsync(); };
            var archive = new Button { Text = "Remover do mural" }; archive.Clicked += async (_, _) =>
            {
                if (!await DisplayAlert("Remover cartão?", "A imagem será arquivada no histórico local.", "Remover", "Cancelar")) return;
                await store.SetDreamBoardArchivedAsync(item.Id, true); await RefreshAsync();
            };
            var links = new List<View>();
            if (!string.IsNullOrWhiteSpace(item.Title)) links.Add(new Label { Text = item.Title, FontSize = 20, FontAttributes = FontAttributes.Bold });
            if (!string.IsNullOrWhiteSpace(item.Description)) links.Add(PageLayout.Muted(item.Description));
            links.Add(PageLayout.Muted(item.GoalId is null && item.ActionCardId is null ? "Sem vínculo" : "Vínculo salvo"));
            links.Add(image); links.Add(new HorizontalStackLayout { Spacing = 8, Children = { up, down, edit, archive } });
            list.Add(PageLayout.Card(links.ToArray()));
        }
    }
    private async Task AddAsync()
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Escolha uma imagem" });
            if (file is null) return;
            var folder = Path.Combine(FileSystem.AppDataDirectory, "dreams"); Directory.CreateDirectory(folder);
            var destination = Path.Combine(folder, $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
            await using var input = await file.OpenReadAsync(); await using var output = File.Create(destination); await input.CopyToAsync(output);
            await EditAsync(new DreamBoardItem(Guid.NewGuid(), destination, null, null, null, null, null, items.Count(i => !i.IsArchived), DreamCardSize.Medium));
        }
        catch (Exception ex) { await DisplayAlert("Não foi possível adicionar", ex.Message, "Entendi"); }
    }
    private async Task EditAsync(DreamBoardItem item) => await Navigation.PushModalAsync(new NavigationPage(new DreamBoardEditorPage(store, goals, item, RefreshAsync)));
}

public sealed class DreamBoardEditorPage : ContentPage
{
    public DreamBoardEditorPage(IDreamBoardStore store, GoalViewModel goals, DreamBoardItem item, Func<Task> saved)
    {
        Title = "Cartão do sonho";
        var title = new Entry { Placeholder = "Título opcional", Text = item.Title, MaxLength = 120 };
        var description = new Editor { Placeholder = "Descrição opcional", Text = item.Description, MaxLength = 2000, HeightRequest = 100 };
        var size = new Picker { Title = "Tamanho", ItemsSource = new[] { "Pequeno", "Médio", "Grande" }, SelectedIndex = (int)item.Size };
        var goal = new Picker { Title = "Meta opcional", ItemsSource = new[] { "Sem meta" }.Concat(goals.Goals.Where(g => !g.IsArchived).Select(g => g.Title)).ToList() };
        goal.SelectedIndex = item.GoalId is { } id ? Math.Max(0, goals.Goals.ToList().FindIndex(g => g.Id == id) + 1) : 0;
        var cardTitles = new[] { "Sem cartão" }.Concat(goals.Cards.Where(c => !c.IsArchived).Select(c => c.Title)).ToList();
        var card = new Picker { Title = "Plano de Ação opcional", ItemsSource = cardTitles, SelectedIndex = item.ActionCardId is { } cardId ? Math.Max(0, goals.Cards.ToList().FindIndex(c => c.Id == cardId) + 1) : 0 };
        var error = PageLayout.Muted("");
        var cancel = new ToolbarItem { Text = "Cancelar" }; cancel.Clicked += async (_, _) => await Navigation.PopModalAsync(); ToolbarItems.Add(cancel);
        var save = new Button { Text = "Salvar cartão" }; save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var selectedGoal = goal.SelectedIndex > 0 ? goals.Goals.Where(g => !g.IsArchived).ElementAt(goal.SelectedIndex - 1).Id : (Guid?)null;
                var selectedCard = card.SelectedIndex > 0 ? goals.Cards.Where(c => !c.IsArchived).ElementAt(card.SelectedIndex - 1).Id : (Guid?)null;
                var updated = item with { Title = string.IsNullOrWhiteSpace(title.Text) ? null : title.Text.Trim(), Description = string.IsNullOrWhiteSpace(description.Text) ? null : description.Text.Trim(),
                    GoalId = selectedGoal, ActionCardId = selectedCard, Size = (DreamCardSize)Math.Max(0, size.SelectedIndex) };
                await store.SaveDreamBoardItemAsync(updated); await Navigation.PopModalAsync(); await saved();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading(Title), new Image { Source = File.Exists(item.ImagePath) ? ImageSource.FromFile(item.ImagePath) : null, HeightRequest = 180, Aspect = Aspect.AspectFit },
            PageLayout.Card(new Label { Text = "Título" }, title, new Label { Text = "Descrição" }, description, new Label { Text = "Tamanho" }, size,
                new Label { Text = "Meta" }, goal, new Label { Text = "Plano de Ação" }, card, error, save));
    }
}

public sealed class DreamImagePage : ContentPage
{
    public DreamImagePage(DreamBoardItem item)
    {
        Title = item.Title ?? "Imagem";
        var close = new ToolbarItem { Text = "Fechar" }; close.Clicked += async (_, _) => await Navigation.PopModalAsync(); ToolbarItems.Add(close);
        Content = new Grid { Children = { new Image { Source = File.Exists(item.ImagePath) ? ImageSource.FromFile(item.ImagePath) : null, Aspect = Aspect.AspectFit } } };
    }
}
