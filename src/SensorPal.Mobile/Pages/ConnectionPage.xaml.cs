using SensorPal.Mobile.Infrastructure;

namespace SensorPal.Mobile.Pages;

public partial class ConnectionPage : ContentPage
{
    readonly SensorPalClient _client;

    public ConnectionPage(SensorPalClient client)
    {
        InitializeComponent();
        _client = client;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ServerUrlEntry.Placeholder = _client.ConfiguredBaseUrl;
        ServerUrlEntry.Text = Preferences.Get(PreferencesKeys.ServerUrl, "");
        ApiKeyEntry.Text = Preferences.Get(PreferencesKeys.ApiKey, "");
    }

    void OnCancelClicked(object? sender, EventArgs e)
        => _ = Navigation.PopModalAsync();

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        SaveButton.IsEnabled = false;
        _client.SetBaseUrl(ServerUrlEntry.Text);
        _client.SetApiKey(ApiKeyEntry.Text.Trim());
        SaveButton.Text = "Saved ✓";
        await Task.Delay(800);
        await Navigation.PopModalAsync();
    }
}
