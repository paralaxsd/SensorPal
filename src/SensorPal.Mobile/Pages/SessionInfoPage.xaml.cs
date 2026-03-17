using SensorPal.Shared.Models;

namespace SensorPal.Mobile.Pages;

public partial class SessionInfoPage : ContentPage
{
    public SessionInfoPage()
    {
        InitializeComponent();
    }

    public void Load(MonitoringSessionDto session)
    {
        PathEntry.Text = session.AudioFilePath ?? "—";
        SizeLabel.Text = session.AudioSizeText is { Length: > 0 } s ? s : "—";
        BitrateLabel.Text = session.AudioBitRateText is { Length: > 0 } b ? b : "—";
    }

    async void OnCloseClicked(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();
}
