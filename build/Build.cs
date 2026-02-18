using System;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions(
    "ci-android",
    GitHubActionsImage.UbuntuLatest,
    On = [GitHubActionsTrigger.Push, GitHubActionsTrigger.WorkflowDispatch],
    InvokedTargets = [nameof(PublishAndroid)],
    FetchDepth = 0)]
[GitHubActions(
    "ci-server",
    GitHubActionsImage.WindowsLatest,
    On = [GitHubActionsTrigger.Push, GitHubActionsTrigger.WorkflowDispatch],
    InvokedTargets = [nameof(Compile)],
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

    readonly Lazy<Tool?> _adbTool = new(TryResolveAdbTool);

    /******************************************************************************************
     * PROPERTIES
     * ***************************************************************************************/
    Tool? AdbTool => _adbTool.Value;
    AbsolutePath SolutionFile => RootDirectory / "SensorPal.slnx";
    AbsolutePath MobileProject => RootDirectory / "src" / "SensorPal.Mobile" / "SensorPal.Mobile.csproj";
    AbsolutePath ArtifactsDir => RootDirectory / "artifacts";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() => DotNetClean(s => s
            .SetProject(SolutionFile)
            .SetConfiguration(Configuration)));

    Target InstallWorkloads => _ => _
        .OnlyWhenDynamic(() => !IsLocalBuild)
        .Executes(() => DotNet("workload install maui-android"));

    Target Restore => _ => _
        .DependsOn(InstallWorkloads)
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

    Target PublishAndroid => _ => _
        .DependsOn(Restore)
        .Executes(() => DotNetBuild(s => s
            .SetProjectFile(MobileProject)
            .SetConfiguration(Configuration.Release)
            .SetFramework("net10.0-android")
            .EnableNoRestore()
            .SetOutputDirectory(ArtifactsDir / "android")));

    Target DeployAndroid => _ => _
        .Executes(DeployToAndroidDevice);

    Target ListAndroidDevices => _ => _
        .Executes(ListAllAndroidDevices);

    /******************************************************************************************
     * METHODS
     * ***************************************************************************************/
    public static int Main() => Execute<Build>(x => x.Compile);

    void DeployToAndroidDevice()
    {
        TryKillAdbServer();

        var deviceArg = string.IsNullOrWhiteSpace(DeviceId) ?
            "" : $" --device-id {DeviceId}";
        DotNet($"build {MobileProject} -t:Install -f net10.0-android -c {Configuration}{deviceArg}");
    }

    void ListAllAndroidDevices()
    {
        TryKillAdbServer();
        if (AdbTool is { } adb)
        {
            adb("devices");
        }
        else
        {
            Log.Warning("Could not find adb tool, skipping device listing. " +
                "If you encounter deployment issues, ensure that adb is installed and ANDROID_HOME or ANDROID_SDK_ROOT environment variables are set.");
        }
    }

    void TryKillAdbServer()
    {
        if (AdbTool is { } adb)
        {
            Log.Information("Killing adb server...");

            adb("kill-server");
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
