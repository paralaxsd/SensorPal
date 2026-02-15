using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    AbsolutePath SolutionFile => RootDirectory / "SensorPal.slnx";
    AbsolutePath MobileProject => RootDirectory / "src" / "SensorPal.Mobile" / "SensorPal.Mobile.csproj";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() => DotNetClean(s => s
            .SetProject(SolutionFile)
            .SetConfiguration(Configuration)));

    Target Restore => _ => _
        .Executes(() => DotNetRestore(s => s
            .SetProjectFile(SolutionFile)));

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() => DotNetBuild(s => s
            .SetProjectFile(SolutionFile)
            .SetConfiguration(Configuration)
            .EnableNoRestore()));

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() => DotNetTest(s => s
            .SetProjectFile(SolutionFile)
            .SetConfiguration(Configuration)
            .EnableNoRestore()
            .EnableNoBuild()));

    Target DeployAndroid => _ => _
        .Executes(() => DotNet(
            $"build {MobileProject} -t:Install -f net10.0-android -c {Configuration}"));
}
