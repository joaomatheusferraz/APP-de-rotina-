using Android.Content;
using Android.Provider;
using Rotina.Domain;

namespace Rotina.Mobile;

internal static class AndroidCalendar
{
    public static void OpenInsert(Appointment item)
    {
        var intent = new Intent(Intent.ActionInsert);
        intent.SetData(CalendarContract.Events.ContentUri);
        intent.PutExtra(CalendarContract.IEventsColumns.Title, item.Title);
        intent.PutExtra(CalendarContract.IEventsColumns.EventLocation, item.Location ?? "");
        intent.PutExtra(CalendarContract.IEventsColumns.Description, item.Note ?? "");
        intent.PutExtra("beginTime", item.StartsAt.ToUnixTimeMilliseconds());
        intent.PutExtra("endTime", item.StartsAt.AddMinutes(item.DurationMinutes).ToUnixTimeMilliseconds());
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Abra o aplicativo para acessar o calendário.");
        activity.StartActivity(intent);
    }
}
