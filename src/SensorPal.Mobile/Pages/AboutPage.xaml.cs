using System.Runtime.CompilerServices;

namespace SensorPal.Mobile.Pages;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        var infoVersion = ThisAssembly.AssemblyInformationalVersion;
        var semver = infoVersion.Contains('+') ? infoVersion[..infoVersion.IndexOf('+')] : infoVersion;
        VersionLabel.Text = $"Version {semver}";
        CommitLabel.Text = $"{ThisAssembly.GitCommitIdShort} on {ThisAssembly.GitBranch}";
        
        var buildDate = ThisAssembly.GitCommitDate.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        BuiltLabel.Text =  $"Built at {buildDate}";
        RepoLinkLabel.Text = ThisAssembly.RepositoryUrl;
#if WINDOWS
        RuntimeLabel.IsVisible = false;
#else
        RuntimeLabel.Text = RuntimeFeature.IsDynamicCodeCompiled ? "AOT" : "Interpreter";
#endif
    }

    async void OnCloseClicked(object? sender, EventArgs e)
        => await Navigation.PopModalAsync();

    async void OnRepoLinkTapped(object? sender, TappedEventArgs e)
        => await Launcher.OpenAsync(ThisAssembly.RepositoryUrl);

    async void OnCommitLinkTapped(object? sender, TappedEventArgs e)
        => await Launcher.OpenAsync(ThisAssembly.GitCommitUrl);
}
