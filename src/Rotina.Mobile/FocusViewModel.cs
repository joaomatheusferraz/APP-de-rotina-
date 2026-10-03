using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class FocusViewModel(IFocusStore store)
{
    public IReadOnlyList<FocusSession> Sessions { get; private set; } = [];
    public string? Error { get; private set; }
    public FocusSession? Open => Sessions.LastOrDefault(s => s.EndedAt is null);
    public async Task LoadAsync()
    {
        try { Sessions = await store.GetFocusSessionsAsync(); Error = null; }
        catch { Error = "Não foi possível abrir o histórico de foco. Seus dados foram preservados."; }
    }
    public async Task StartAsync(string title, string category, int targetMinutes)
    {
        await store.StartFocusAsync(new FocusSession(Guid.NewGuid(), title, category, targetMinutes,
            DateTimeOffset.UtcNow, null, null, 0, null, null, null)); await LoadAsync();
    }
    public async Task PauseAsync(FocusSession session) { await store.PauseFocusAsync(session.Id, DateTimeOffset.UtcNow); await LoadAsync(); }
    public async Task ResumeAsync(FocusSession session) { await store.ResumeFocusAsync(session.Id, DateTimeOffset.UtcNow); await LoadAsync(); }
    public async Task FinishAsync(FocusSession session, int? rating, string? note)
    { await store.FinishFocusAsync(session.Id, DateTimeOffset.UtcNow, rating, note); await LoadAsync(); }
}
