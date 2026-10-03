using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class DashboardSettingsPage : ContentPage
{
    private readonly IDashboardStore store;
    private readonly Action<IReadOnlyList<DashboardCardSetting>> onSaved;
    private readonly VerticalStackLayout rows = new() { Spacing = 10 };
    private List<DashboardCardSetting> settings = [];
    private static readonly IReadOnlyDictionary<string,string> Labels = new Dictionary<string,string>
    { ["habits"]="Hábitos pendentes", ["planning"]="Agenda", ["workout"]="Treino", ["water"]="Água", ["checkin"]="Check-in" };
    public DashboardSettingsPage(IDashboardStore store, Action<IReadOnlyList<DashboardCardSetting>> onSaved)
    {
        this.store = store; this.onSaved = onSaved; Title = "Personalizar painel";
        var save = new Button { Text = "Salvar painel" }; save.Clicked += async (_,_) => await SaveAsync();
        var reset = new Button { Text = "Restaurar layout padrão" }; reset.Clicked += (_,_) => { settings = Defaults(); Render(); };
        Content = PageLayout.Body(PageLayout.Heading("Seu painel"), PageLayout.Muted("Escolha os cartões visíveis e ajuste a ordem."), rows, save, reset);
    }
    protected override async void OnAppearing() { base.OnAppearing(); settings = (await store.GetDashboardSettingsAsync()).ToList(); Render(); }
    private List<DashboardCardSetting> Defaults() => [new("habits",0,true),new("planning",1,true),new("workout",2,true),new("water",3,true),new("checkin",4,true)];
    private void Render()
    {
        rows.Clear();
        for (var i=0;i<settings.Count;i++) { var index=i; var item=settings[i]; var sw=new Switch { IsToggled=item.IsVisible, HorizontalOptions=LayoutOptions.End }; sw.Toggled += (_,e)=>settings[index]=item with { IsVisible=e.Value };
            var up=new Button { Text="↑", IsEnabled=i>0, WidthRequest=52 }; up.Clicked += (_,_) => Move(index,-1); var down=new Button { Text="↓", IsEnabled=i<settings.Count-1, WidthRequest=52 }; down.Clicked += (_,_) => Move(index,1);
            var grid=new Grid { ColumnDefinitions=new ColumnDefinitionCollection { new(GridLength.Star),new(GridLength.Auto),new(GridLength.Auto),new(GridLength.Auto) }, ColumnSpacing=6 }; grid.Add(new Label { Text=Labels.TryGetValue(item.Key,out var label)?label:item.Key, VerticalOptions=LayoutOptions.Center, FontSize=17 },0,0); grid.Add(sw,1,0); grid.Add(up,2,0); grid.Add(down,3,0); rows.Add(PageLayout.Card(grid)); }
    }
    private void Move(int index,int delta) { var other=index+delta; if(other<0||other>=settings.Count)return; (settings[index],settings[other])=(settings[other],settings[index]); Render(); }
    private async Task SaveAsync() { await store.SaveDashboardSettingsAsync(settings); onSaved(settings); await Navigation.PopModalAsync(); }
}
