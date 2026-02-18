namespace SensorPal.Mobile.Pages;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version {AppInfo.VersionString}";
        BuildLabel.Text = $"Build {AppInfo.BuildString}";
    }

    async void OnCloseClicked(object? sender, EventArgs e)
        => await Navigation.PopModalAsync();

    async void OnRepoLinkTapped(object? sender, TappedEventArgs e)
        => await Launcher.OpenAsync("https://github.com/paralaxsd/SensorPal");
}
