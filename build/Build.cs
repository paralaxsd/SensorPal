using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
// ReSharper disable UnusedMember.Local
// ReSharper disable AllUnderscoreLocalParameterName

[GitHubActions(
    "ci-android", GitHubActionsImage.UbuntuLatest,
    On = [GitHubActionsTrigger.Push, GitHubActionsTrigger.WorkflowDispatch],
    InvokedTargets = [nameof(PublishAndroid)], PublishArtifacts = true,
    FetchDepth = 0)]
[GitHubActions(
    "tests", GitHubActionsImage.WindowsLatest,
    On = [GitHubActionsTrigger.Push, GitHubActionsTrigger.WorkflowDispatch],
    InvokedTargets = [nameof(Test)],
    FetchDepth = 0)]
sealed class Build : NukeBuild
{
    /******************************************************************************************
     * FIELDS
     * ***************************************************************************************/
    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Android device id for deployment (use 'adb devices' to list)")]
    readonly string? DeviceId;

    [Parameter("Enable AOT compilation for Android deployment")]
    readonly bool Aot;

    readonly Lazy<Tool?> LazyAdbTool = new(TryResolveAdbTool);

    /******************************************************************************************
     * PROPERTIES
     * ***************************************************************************************/
    Tool? AdbTool => LazyAdbTool.Value;
    AbsolutePath SolutionFile => RootDirectory / "SensorPal.slnx";
    AbsolutePath MobileProject => RootDirectory / "src" / "SensorPal.Mobile" / "SensorPal.Mobile.csproj";
    AbsolutePath ServerProject => RootDirectory / "src" / "SensorPal.Server" / "SensorPal.Server.csproj";
    AbsolutePath ArtifactsDir => RootDirectory / "artifacts";
    AbsolutePath TestsDir => RootDirectory / "tests";

    Target Clean => _ => _
        .Description("Remove all build outputs")
        .Before(Restore)
        .Executes(() => DotNetClean(s => s
            .SetProject(SolutionFile)
            .SetConfiguration(Configuration)));

    Target InstallWorkloads => _ => _
        .Description("Install the maui-android workload (CI only)")
        .OnlyWhenDynamic(() => !IsLocalBuild)
        .Executes(() => DotNet("workload install maui-android"));

    Target Restore => _ => _
        .Description("Restore NuGet packages for the entire solution")
        .DependsOn(InstallWorkloads)
        .Executes(() => DotNetRestore(s => s
            .SetProjectFile(SolutionFile)));

    Target Compile => _ => _
        .Description("Build the entire solution (server + mobile + shared)")
        .DependsOn(Restore)
        .Executes(() => DotNetBuild(s => s
            .SetProjectFile(SolutionFile)
            .SetConfiguration(Configuration)
            .EnableNoRestore()));

    Target Test => _ => _
        .Description("Build and run all test projects discovered under tests/")
        .Executes(() =>
        {
            var testProjects = TestsDir.GlobFiles("**/*Tests*.csproj");
            DotNetTest(s => s
                .SetConfiguration(Configuration)
                .CombineWith(testProjects, (s, p) => s.SetProjectFile(p)));
        });

    Target PublishAndroid => _ => _
        .Description("Build a Release Android APK and place it in artifacts/android")
        .DependsOn(Restore)
        .Produces(RootDirectory / "artifacts" / "android")
        .Executes(() => DotNetBuild(s => s
            .SetProjectFile(MobileProject)
            .SetConfiguration(Configuration.Release)
            .SetFramework("net10.0-android")
            .EnableNoRestore()
            .SetOutputDirectory(ArtifactsDir / "android")));

    Target PublishServer => _ => _
        .Description("Publish a self-contained Release server build to artifacts/server")
        .DependsOn(Restore)
        .Produces(RootDirectory / "artifacts" / "server")
        .Executes(() => DotNetPublish(s => s
            .SetProject(ServerProject)
            .SetConfiguration(Configuration.Release)
            .EnableNoRestore()
            .SetOutput(ArtifactsDir / "server")));

    Target DeployAndroid => _ => _
        .Description("Build and deploy the Android app to a connected device via ADB (use --device-id to target a specific device, --aot for AOT)")
        .Executes(DeployToAndroidDevice);

    Target ListAndroidDevices => _ => _
        .Description("List all Android devices currently visible to ADB")
        .Executes(ListAllAndroidDevices);

    /******************************************************************************************
     * METHODS
     * ***************************************************************************************/
    public static int Main() => Execute<Build>(x => x.Compile);

    void DeployToAndroidDevice()
    {
        TryKillAdbServer();

        DotNetClean(s => s
            .SetProject(MobileProject)
            .SetConfiguration(Configuration));

        // The Android Install target is documented at https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-targets#install
        // Also note that we embed deviceArg into the DotNet call using the "nq" format specifier to avoid double quoting -
        // we want the final command to look like: dotnet build /p:AdbTarget="-s <device id>".
        // We further disable the Mono interpreter for AOT builds.
        // Also see: https://learn.microsoft.com/en-us/dotnet/maui/macios/interpreter?view=net-maui-10.0 and
        // https://learn.microsoft.com/en-us/dotnet/android/messages/xa0119
        var deployToDefaultDevice = string.IsNullOrWhiteSpace(DeviceId);
        var deviceArg = deployToDefaultDevice ?
            "" : $" /p:AdbTarget=\"-s {DeviceId}\"";
        var aotArg = Aot ? " /p:RunAOTCompilation=true /p:EmbedAssembliesIntoApk=true /p:PublishTrimmed=true /p:UseInterpreter=false" : "";

        if (!deployToDefaultDevice)
        {
            Log.Information("Deploying to device with ID: {DeviceId}", DeviceId);
        }
        if (Aot)
        {
            Log.Information("AOT compilation enabled as per user request.");
        }

        DotNet($"build {MobileProject} -t:Clean -t:Install -f net10.0-android -c {Configuration}{deviceArg:nq}{aotArg:nq}");
    }

    void ListAllAndroidDevices()
    {
        TryRestartAdbServer();

        RunAdbCommand(adb => adb("devices"));
    }

    void TryKillAdbServer() => RunAdbCommand(adb =>
    {
        Log.Information("Killing adb server...");
        adb("kill-server");
    });

    void TryRestartAdbServer() => RunAdbCommand(adb =>
    {
        Log.Information("** Restarting ADB server...**");
        adb("kill-server");

        Log.Information("Giving ADB some time before restarting...");
        // otherwise it may throw: "daemon not running; starting now at tcp:5037"
        Thread.Sleep(250);
        adb("start-server");
    });

    void RunAdbCommand(Action<Tool> run)
    {
        if (AdbTool is { } adb)
        {
            run(adb);
        }
        else
        {
            Log.Warning("Could not find adb tool, skipping adb server kill. " +
                "If you encounter deployment issues, ensure that adb is installed and ANDROID_HOME or ANDROID_SDK_ROOT environment variables are set.");
        }
    }

    static Tool? TryResolveAdbTool()
    {
        var programX86Path = Environment.GetEnvironmentVariable("ProgramFiles(x86)");

        string?[] possiblePaths =
        [
            Environment.GetEnvironmentVariable("ANDROID_HOME"),
            Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
            programX86Path == null ? null : Path.Join(programX86Path, "Android", "android-sdk")
        ];

        var adbPath = possiblePaths.WhereNotNull()
            .Select(p => Path.Join(p, "platform-tools", "adb.exe"))
            .FirstOrDefault(File.Exists);

        return adbPath is { } ? ToolResolver.GetTool(adbPath) : null;
    }
}
