using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class PlanningViewModel(IPlanningStore store, ILocalStore local)
{
    public IReadOnlyList<PlannedTask> Tasks { get; private set; } = [];
    public IReadOnlyList<Appointment> Appointments { get; private set; } = [];
    public IReadOnlyList<LifeArea> Areas { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task LoadAsync()
    {
        try
        {
            Tasks = await store.GetTasksAsync();
            Appointments = await store.GetAppointmentsAsync();
            Areas = await local.GetAreasAsync();
            Error = null;
        }
        catch (Exception) { Error = "Não foi possível abrir tarefas e compromissos. Seus dados foram preservados."; }
    }

    public async Task SaveTaskAsync(PlannedTask task)
    {
        await store.SaveTaskAsync(task);
        await LoadAsync();
    }
    public async Task CompleteTaskAsync(PlannedTask task)
    {
        await store.SetTaskCompletedAsync(task.Id, task.CompletedAt is null ? DateTimeOffset.UtcNow : null);
        await LoadAsync();
    }
    public async Task ArchiveTaskAsync(PlannedTask task, bool archived)
    {
        await store.SetTaskArchivedAsync(task.Id, archived);
        await LoadAsync();
    }
    public async Task SaveAppointmentAsync(Appointment appointment)
    {
        await store.SaveAppointmentAsync(appointment);
        await LoadAsync();
    }
    public async Task ArchiveAppointmentAsync(Appointment appointment, bool archived)
    {
        await store.SetAppointmentArchivedAsync(appointment.Id, archived);
        await LoadAsync();
    }
}
