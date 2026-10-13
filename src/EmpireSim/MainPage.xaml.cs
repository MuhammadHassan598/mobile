using EmpireSim.Core.Services;

namespace EmpireSim;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The device's Back (the Android button or gesture) does exactly what the Back arrow in the app does: close the topmost popup or card, else
    /// return to the screen the player came from. On the main screen with nothing open there is nothing to go back to, and the system's own Back
    /// applies (leaving the app).
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        var history = Handler?.MauiContext?.Services.GetService<NavigationHistory>();
        if (history is null || !history.CanGoBack) return base.OnBackButtonPressed();
        return history.Back() != BackOutcome.None || base.OnBackButtonPressed();
    }
}
