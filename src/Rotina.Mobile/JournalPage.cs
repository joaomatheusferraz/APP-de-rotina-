using Rotina.Application;
using Rotina.Domain;
namespace Rotina.Mobile;
public sealed class JournalPage : ContentPage
{
    private readonly IJournalStore store; private readonly VerticalStackLayout list=new(){Spacing=12};
    public JournalPage(IJournalStore store){this.store=store;Title="Diário";var add=new Button{Text="+ Nova nota"};add.Clicked+=async(_,_)=>await EditAsync(null);Content=PageLayout.Body(PageLayout.Heading("Diário"),PageLayout.Muted("Registre o contexto que os números não capturam."),add,list);}
    protected override async void OnAppearing(){base.OnAppearing();await LoadAsync();}
    private async Task LoadAsync(){list.Clear();foreach(var entry in await store.GetJournalEntriesAsync()){var edit=new Button{Text="Editar"};edit.Clicked+=async(_,_)=>await EditAsync(entry);var del=new Button{Text="Excluir"};del.Clicked+=async(_,_)=>{if(await DisplayAlert("Excluir nota?","O registro será removido.","Excluir","Cancelar")){await store.DeleteJournalEntryAsync(entry.Id);await LoadAsync();}};list.Add(PageLayout.Card(new Label{Text=entry.LocalDate.ToString("dd/MM/yyyy"),FontAttributes=FontAttributes.Bold},PageLayout.Muted(entry.Text),edit,del));}if(list.Count==0)list.Add(PageLayout.Muted("Nenhuma nota registrada."));}
    private async Task EditAsync(JournalEntry? existing){var text=await DisplayPromptAsync("Nota do diário","O que aconteceu hoje?","Salvar","Cancelar",maxLength:4000,initialValue:existing?.Text??"");if(text is null)return;var date=await DisplayPromptAsync("Data","Use AAAA-MM-DD.","Salvar","Cancelar",initialValue:(existing?.LocalDate??DateOnly.FromDateTime(DateTime.Today)).ToString("yyyy-MM-dd"));if(date is null||!DateOnly.TryParse(date,out var local)){await DisplayAlert("Data inválida","Informe uma data válida.","Entendi");return;}try{await store.SaveJournalEntryAsync(new(existing?.Id??Guid.NewGuid(),local,text,existing?.CreatedAt??DateTimeOffset.UtcNow,existing is null?null:DateTimeOffset.UtcNow));await LoadAsync();}catch(Exception ex){await DisplayAlert("Nota inválida",ex.Message,"Entendi");}}
}
