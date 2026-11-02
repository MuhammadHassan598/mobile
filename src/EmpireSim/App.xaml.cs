using EmpireSim.Core.Services;

namespace EmpireSim;

public partial class App : Application
{
    private readonly GameEngine? _engine;

    public App() : this(null) { }

    public App(GameEngine? engine)
    {
        _engine = engine;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        window.Deactivated += (_, _) => _ = _engine?.AutoSaveAsync();
        window.Stopped += (_, _) => _ = _engine?.AutoSaveAsync();
        window.Destroying += (_, _) => _ = _engine?.AutoSaveAsync();
        return window;
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        _ = _engine?.AutoSaveAsync();
    }
}
