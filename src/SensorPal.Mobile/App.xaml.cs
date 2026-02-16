namespace SensorPal.Mobile;

public partial class App : Application
{
    readonly AppShell _shell;

    public App(AppShell shell)
    {
        _shell = shell;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new Window(_shell);
}
