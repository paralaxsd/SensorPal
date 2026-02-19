namespace SensorPal.Mobile.Pages;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version {AppInfo.VersionString}";
        CommitLabel.Text = $"{ThisAssembly.GitCommitIdShort} on {ThisAssembly.GitBranch}";
        BuiltLabel.Text = ThisAssembly.GitCommitDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        RepoLinkLabel.Text = ThisAssembly.RepositoryUrl;
    }

    async void OnCloseClicked(object? sender, EventArgs e)
        => await Navigation.PopModalAsync();

    async void OnRepoLinkTapped(object? sender, TappedEventArgs e)
        => await Launcher.OpenAsync(ThisAssembly.RepositoryUrl);

    async void OnCommitLinkTapped(object? sender, TappedEventArgs e)
        => await Launcher.OpenAsync(ThisAssembly.GitCommitUrl);
}
