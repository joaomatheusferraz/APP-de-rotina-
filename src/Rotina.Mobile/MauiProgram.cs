using Rotina.Application;
using Rotina.Infrastructure;

namespace Rotina.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });
        builder.Services.AddSingleton<ILocalStore>(_ => new SqliteLocalStore(Path.Combine(FileSystem.AppDataDirectory, "rotina.db")));
        builder.Services.AddSingleton<IBackupService>(provider => new LocalBackupService((SqliteLocalStore)provider.GetRequiredService<ILocalStore>(), Path.Combine(FileSystem.AppDataDirectory, "rotina.db")));
        builder.Services.AddSingleton<FoundationViewModel>();
        builder.Services.AddSingleton<IHabitStore>(provider => (IHabitStore)provider.GetRequiredService<ILocalStore>());
        builder.Services.AddSingleton<HabitViewModel>();
        builder.Services.AddSingleton<IDailyStore>(provider => (IDailyStore)provider.GetRequiredService<ILocalStore>());
        builder.Services.AddSingleton<DailyViewModel>();
        builder.Services.AddSingleton<IPlanningStore>(provider => (IPlanningStore)provider.GetRequiredService<ILocalStore>());
        builder.Services.AddSingleton<PlanningViewModel>();
        builder.Services.AddSingleton<IGoalStore>(provider => (IGoalStore)provider.GetRequiredService<ILocalStore>());
        builder.Services.AddSingleton<GoalViewModel>();
        builder.Services.AddSingleton<IBodyStore>(provider => (IBodyStore)provider.GetRequiredService<ILocalStore>());
        builder.Services.AddSingleton<IWorkoutStore>(provider => (IWorkoutStore)provider.GetRequiredService<ILocalStore>());
        builder.Services.AddSingleton<WorkoutViewModel>();
        builder.Services.AddSingleton<IFocusStore>(provider => (IFocusStore)provider.GetRequiredService<ILocalStore>());
        builder.Services.AddSingleton<FocusViewModel>();
        builder.Services.AddSingleton<IDreamBoardStore>(provider => (IDreamBoardStore)provider.GetRequiredService<ILocalStore>());
        builder.Services.AddSingleton<IDashboardStore>(provider => (IDashboardStore)provider.GetRequiredService<ILocalStore>());
        builder.Services.AddSingleton<IJournalStore>(provider => (IJournalStore)provider.GetRequiredService<ILocalStore>());
        builder.Services.AddSingleton<AppShell>();
        return builder.Build();
    }
}
