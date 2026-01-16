using SensorPal.Mobile.Infrastructure;

namespace SensorPal.Mobile;

public partial class MainPage : ContentPage
{
    int count = 0;
    readonly SensorPalClient _client;

    public MainPage(SensorPalClient client)
    {
        _client = client;

        InitializeComponent();
        _ = UpdateStatusAsync();
    }

    async void OnCounterClicked(object? sender, EventArgs e)
    {
        count++;
        CounterBtn.Text = count == 1 ? $"Clicked {count} time" : $"Clicked {count} times";
        SemanticScreenReader.Announce(CounterBtn.Text);

        await UpdateStatusAsync();
    }

    async Task UpdateStatusAsync()
    {
        try
        {
            var status = await _client.GetStatusAsync();

            StatusLabel.Text = $"🟢 {status}";
            StatusLabel.TextColor = Colors.Green;
        }
        catch
        {
            StatusLabel.Text = "🔴 offline";
            StatusLabel.TextColor = Colors.Red;
        }
    }
}