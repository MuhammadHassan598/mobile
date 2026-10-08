using EmpireSim.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<SimulationService>();
builder.Services.AddSingleton(_ => new SaveService(Path.Combine(Path.GetTempPath(), "empiresim-preview")));
builder.Services.AddSingleton<GameEngine>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<EmpireSim.PreviewApp>().AddInteractiveServerRenderMode();

// Dev-only: start a campaign so the main menu can be previewed.
app.MapGet("/dev/start", (GameEngine engine) =>
{
    engine.StartCampaign("france");
    return Results.Redirect("/");
});

app.Run();
