using EmpireSim.Core.Services;
using Microsoft.Extensions.Logging;

namespace EmpireSim;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // ---- Game services (singletons: one campaign per app session) ----
        builder.Services.AddSingleton<SimulationService>();
        builder.Services.AddSingleton(_ => new SaveService(FileSystem.AppDataDirectory));
        builder.Services.AddSingleton<GameEngine>();

        return builder.Build();
    }
}
