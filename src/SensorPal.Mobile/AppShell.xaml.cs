using SensorPal.Mobile.Infrastructure;
using SensorPal.Mobile.Pages;

namespace SensorPal.Mobile;

public partial class AppShell
{
    /******************************************************************************************
     * FIELDS
     * ***************************************************************************************/
    readonly ConnectivityDialogService _dialog;
    bool _started;

    /******************************************************************************************
     * STRUCTORS
     * ***************************************************************************************/
    public AppShell(ConnectivityDialogService dialog)
    {
        _dialog = dialog;
        InitializeComponent();
    }

    /******************************************************************************************
     * METHODS
     * ***************************************************************************************/
    public void CheckConnectivityOnResume() => _ = _dialog.CheckOnResumeAsync();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_started) { return; }

        _started = true;
        _dialog.OpenSettingsAsync = OpenSettingsAsync;
        _dialog.Start();
    }

    async Task OpenSettingsAsync()
    {
        var page = Handler!.MauiContext!.Services.GetRequiredService<ConnectionPage>();
        // Wait until the page is actually dismissed, not just pushed.
        // PushModalAsync completes after the push animation — we need to hold the
        // connectivity-dialog loop until the user closes ConnectionPage.
        var tcs = new TaskCompletionSource();
        page.Disappearing += (_, _) => tcs.TrySetResult();
        await Navigation.PushModalAsync(page);
        await tcs.Task;
    }

    async void OnLogsClicked(object? sender, EventArgs e)
        => await Navigation.PushModalAsync(Handler!.MauiContext!.Services.GetRequiredService<LogsPage>());

    async void OnSettingsClicked(object? sender, EventArgs e)
        => await Navigation.PushModalAsync(Handler!.MauiContext!.Services.GetRequiredService<SettingsPage>());

    async void OnAboutClicked(object? sender, EventArgs e)
        => await Navigation.PushModalAsync(new AboutPage());
}
