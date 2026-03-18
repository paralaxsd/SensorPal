using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Serilog;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
// ReSharper disable UnusedMember.Local
// ReSharper disable AllUnderscoreLocalParameterName

[GitHubActions(
    "release", GitHubActionsImage.WindowsLatest,
    OnPushTags = ["v*"],
    InvokedTargets = [nameof(Release)],
    WritePermissions = [GitHubActionsPermissions.Contents],
    FetchDepth = 0)]
[GitHubActions(
    "ci-android", GitHubActionsImage.UbuntuLatest,
    On = [GitHubActionsTrigger.Push, GitHubActionsTrigger.WorkflowDispatch],
    InvokedTargets = [nameof(PublishAndroid)], PublishArtifacts = true,
    FetchDepth = 0)]
[GitHubActions(
    "tests", GitHubActionsImage.WindowsLatest,
    On = [GitHubActionsTrigger.Push, GitHubActionsTrigger.WorkflowDispatch],
    InvokedTargets = [nameof(Test)], PublishArtifacts = true,
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
    AbsolutePath CoverageDir => RootDirectory / "coverage";

    AbsolutePath WindowsClientPublishDir => ArtifactsDir / "windows";
    AbsolutePath ServerPortablePublishDir => ArtifactsDir / "server-portable";
    AbsolutePath WindowsClientZip => ArtifactsDir / "SensorPal-windows-win-x64.zip";
    AbsolutePath ServerZip => ArtifactsDir / "SensorPal-server-win-x64.zip";
    AbsolutePath AndroidZip => ArtifactsDir / "SensorPal-android.zip";

    Target Clean => _ => _
        .Description("Remove all build outputs")
        .Before(Restore)
        .Executes(() => DotNetClean(s => s
            .SetProject(SolutionFile)
            .SetConfiguration(Configuration)));

    Target InstallWorkloads => _ => _
        .Description("Install required MAUI workloads (CI only)")
        .OnlyWhenDynamic(() => !IsLocalBuild)
        .Executes(() =>
        {
            DotNet("workload install maui-android");
            if (OperatingSystem.IsWindows())
                DotNet("workload install maui-windows");
        });

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
        .Produces(CoverageDir / "report")
        .Executes(() =>
        {
            var testProjects = TestsDir.GlobFiles("**/*Tests*.csproj");
            DotNetTest(s => s
                .SetConfiguration(Configuration)
                .AddLoggers("GitHubActions")
                .SetResultsDirectory(CoverageDir)
                .AddProcessAdditionalArguments("--collect:\"XPlat Code Coverage\"")
                .CombineWith(testProjects, (s, p) => s.SetProjectFile(p)));

            PublishCoverageReport();
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

    Target PackWindowsClient => _ => _
        .Description("Publish the MAUI Windows client (self-contained win-x64) and zip into artifacts/")
        .DependsOn(Restore)
        .Produces(WindowsClientZip)
        .Executes(() =>
        {
            DotNetPublish(s => s
                .SetProject(MobileProject)
                .SetConfiguration(Configuration.Release)
                .SetFramework("net10.0-windows10.0.19041.0")
                .SetRuntime("win-x64")
                .SetSelfContained(true)
                .EnableNoRestore()
                .SetOutput(WindowsClientPublishDir));

            WindowsClientPublishDir.ZipTo(WindowsClientZip);
        });

    Target PackServer => _ => _
        .Description("Publish the server (self-contained win-x64) and zip into artifacts/")
        .DependsOn(Restore)
        .Produces(ServerZip)
        .Executes(() =>
        {
            DotNetPublish(s => s
                .SetProject(ServerProject)
                .SetConfiguration(Configuration.Release)
                .SetRuntime("win-x64")
                .SetSelfContained(true)
                .SetOutput(ServerPortablePublishDir));

            ServerPortablePublishDir.ZipTo(ServerZip);
        });

    Target PackAndroid => _ => _
        .Description("Build the Android APK and zip into artifacts/")
        .DependsOn(Restore)
        .Produces(AndroidZip)
        .Executes(() =>
        {
            var androidDir = ArtifactsDir / "android";

            DotNetBuild(s => s
                .SetProjectFile(MobileProject)
                .SetConfiguration(Configuration.Release)
                .SetFramework("net10.0-android")
                .EnableNoRestore()
                .SetOutputDirectory(androidDir));

            var apks = androidDir.GlobFiles("*.apk");
            Assert.NotEmpty(apks, "No APK found in android build output");

            using var stream = File.Create(AndroidZip);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach (var apk in apks)
                archive.CreateEntryFromFile(apk, Path.GetFileName(apk));
        });

    Target Release => _ => _
        .Description("Build all release packages and create a GitHub Release for the current tag")
        .DependsOn(PackWindowsClient, PackServer, PackAndroid)
        .OnlyWhenDynamic(() => IsServerBuild)
        .Executes(() =>
        {
            var tag = Environment.GetEnvironmentVariable("GITHUB_REF_NAME")
                ?? throw new InvalidOperationException(
                    "GITHUB_REF_NAME is not set — this target must run on GitHub Actions with a tag push.");

            var assets = string.Join(" ", new[] { WindowsClientZip, ServerZip, AndroidZip }
                .Select(p => $"\"{(string)p}\""));

            ProcessTasks
                .StartProcess("gh", $"release create \"{tag}\" {assets} --title \"SensorPal {tag}\" --generate-notes",
                    workingDirectory: RootDirectory)
                .AssertZeroExitCode();
        });

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

    void PublishCoverageReport()
    {
        var coberturaFiles = CoverageDir.GlobFiles("**/coverage.cobertura.xml");
        if (coberturaFiles.Count == 0)
        {
            Log.Warning("No Cobertura coverage files found — skipping report generation.");
            return;
        }

        DotNet("tool restore");

        var reportDir = CoverageDir / "report";
        var reports = string.Join(";", coberturaFiles.Select(f => f.ToString()));
        DotNet($"tool run reportgenerator -- -reports:\"{reports}\" -targetdir:\"{reportDir}\" -reporttypes:MarkdownSummaryGithub;Html");

        var summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (summaryFile is { } && File.Exists(reportDir / "SummaryGithub.md"))
            File.AppendAllText(summaryFile, "\n\n" + File.ReadAllText(reportDir / "SummaryGithub.md"));
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
