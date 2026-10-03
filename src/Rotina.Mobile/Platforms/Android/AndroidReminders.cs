using Android.App;
using Android.Content;
using Android.OS;
using Android.Content.PM;
using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Mobile;

internal static class AndroidReminders
{
    internal const string ChannelId = "rotina_planning";
    internal const string KindKey = "kind";
    internal const string IdKey = "item_id";
    internal const string TitleKey = "title";

    public static async Task<bool> AskPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return true;
        return await Permissions.RequestAsync<Permissions.PostNotifications>() == PermissionStatus.Granted;
    }

    public static void Sync(PlannedTask item) => Schedule(item.Id, "task", item.Title,
        item.CompletedAt is null && !item.IsArchived ? item.ReminderAt : null);

    public static void Sync(Appointment item) => Schedule(item.Id, "appointment", item.Title,
        !item.IsArchived ? item.ReminderAt : null);

    public static void SyncAll(PlanningViewModel model)
    {
        foreach (var item in model.Tasks) Sync(item);
        foreach (var item in model.Appointments) Sync(item);
    }

    private static void Schedule(Guid id, string kind, string title, DateTimeOffset? at)
    {
        var context = Android.App.Application.Context;
        var manager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (manager is null) return;
        var intent = new Intent(context, typeof(ReminderReceiver));
        intent.PutExtra(KindKey, kind);
        intent.PutExtra(IdKey, id.ToString());
        intent.PutExtra(TitleKey, title);
        var requestCode = RequestCode(id, kind);
        var pending = PendingIntent.GetBroadcast(context, requestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        if (pending is null) return;
        manager.Cancel(pending);
        if (at is { } reminder && reminder > DateTimeOffset.Now)
            manager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, reminder.ToUnixTimeMilliseconds(), pending);
    }

    internal static int RequestCode(Guid id, string kind)
    {
        var bytes = id.ToByteArray();
        var hash = kind == "task" ? 17 : 31;
        foreach (var value in bytes) hash = unchecked(hash * 31 + value);
        return hash;
    }

}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            context.CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted) return;
        var kind = intent.GetStringExtra(AndroidReminders.KindKey) ?? "task";
        var title = intent.GetStringExtra(AndroidReminders.TitleKey) ?? "Rotina";
        var id = intent.GetStringExtra(AndroidReminders.IdKey) ?? "";
        var notifications = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (notifications is null) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            notifications.CreateNotificationChannel(new NotificationChannel(AndroidReminders.ChannelId, "Lembretes da rotina", NotificationImportance.Default));
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(context, AndroidReminders.ChannelId)
            : new Notification.Builder(context);
        builder.SetContentTitle(title)
            .SetContentText(kind == "task" ? "Lembrete de tarefa" : "Lembrete de compromisso")
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetAutoCancel(true);
        notifications.Notify(AndroidReminders.RequestCode(Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty, kind), builder.Build());
    }
}

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted])]
public sealed class ReminderBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Intent.ActionBootCompleted) return;
        var pending = GoAsync();
        var dataDirectory = context.FilesDir?.AbsolutePath;
        _ = Task.Run(async () =>
        {
            try
            {
                if (dataDirectory is null) return;
                var store = new SqliteLocalStore(Path.Combine(dataDirectory, "rotina.db"));
                foreach (var task in await store.GetTasksAsync()) AndroidReminders.Sync(task);
                foreach (var item in await store.GetAppointmentsAsync()) AndroidReminders.Sync(item);
            }
            catch { /* A próxima abertura do app tenta agendar os lembretes novamente. */ }
            finally { pending?.Finish(); }
        });
    }
}
