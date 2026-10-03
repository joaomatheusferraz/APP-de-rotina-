namespace Rotina.Mobile;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly AppShell shell;
    public App(AppShell shell)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
        this.shell = shell;
    }
    protected override Window CreateWindow(IActivationState? activationState) => new(shell);
}
