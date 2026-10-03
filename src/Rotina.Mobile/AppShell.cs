using Rotina.Application;

namespace Rotina.Mobile;

public sealed class AppShell : Shell
{
    public AppShell(FoundationViewModel model, HabitViewModel habitModel, DailyViewModel dailyModel, PlanningViewModel planningModel, GoalViewModel goalModel, WorkoutViewModel workoutModel, FocusViewModel focusModel, IDreamBoardStore dreamStore, IDashboardStore dashboardStore, IBackupService backup, IJournalStore journal)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        this.SetAppThemeColor(Shell.BackgroundColorProperty, Color.FromArgb("#F4F5FC"), Color.FromArgb("#090E1C"));
        this.SetAppThemeColor(Shell.ForegroundColorProperty, Color.FromArgb("#18213B"), Color.FromArgb("#F3F5FF"));
        this.SetAppThemeColor(Shell.TabBarBackgroundColorProperty, Colors.White, Color.FromArgb("#121C30"));
        this.SetAppThemeColor(Shell.TabBarTitleColorProperty, Color.FromArgb("#6251C6"), Color.FromArgb("#AA9EFF"));
        this.SetAppThemeColor(Shell.TabBarUnselectedColorProperty, Color.FromArgb("#59647E"), Color.FromArgb("#A5B1CC"));
        var tabs = new TabBar();
        AddTab(tabs, "Hoje", "today", "nav_today.svg", () => new TodayPage(model, habitModel, dailyModel, planningModel, workoutModel, dashboardStore));
        AddTab(tabs, "Rotina", "routine", "nav_routine.svg", () => new RoutinePage(habitModel, planningModel, goalModel, focusModel, dreamStore));
        AddTab(tabs, "Treino", "workout", "nav_workout.svg", () => new WorkoutPage(workoutModel));
        AddTab(tabs, "Progresso", "progress", "nav_progress.svg", () => new GoalProgressPage(goalModel, workoutModel));
        AddTab(tabs, "Perfil", "profile", "nav_profile.svg", () => new ProfilePage(model, dailyModel, backup, journal));
        Items.Add(tabs);
    }
    private static void AddTab(TabBar bar, string title, string route, string icon, Func<Page> create)
    {
        var tab = new Tab { Title = title, Route = route, Icon = icon };
        tab.Items.Add(new ShellContent { Title = title, ContentTemplate = new DataTemplate(create) });
        bar.Items.Add(tab);
    }
}
