using CommunityToolkit.Maui.Views;
using SensorPal.Shared.Models;

namespace SensorPal.Mobile.Pages;

public partial class SessionPlayerPage : ContentPage
{
    public SessionPlayerPage()
    {
        InitializeComponent();
    }

    public void Load(MonitoringSessionDto session, string audioUrl)
    {
        TitleLabel.Text = session.StartedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
        Player.Source = MediaSource.FromUri(audioUrl);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Player.Stop();
    }

    async void OnCloseClicked(object? sender, EventArgs e)
    {
        Player.Stop();
        await Navigation.PopModalAsync();
    }
}
