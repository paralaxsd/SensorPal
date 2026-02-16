using System;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

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
        .Executes(DeployToAndroidDevice);

    /******************************************************************************************
     * METHODS
     * ***************************************************************************************/
    public static int Main() => Execute<Build>(x => x.Compile);

    void DeployToAndroidDevice()
    {
        TryRestartAdbServer();

        var deviceArg = string.IsNullOrWhiteSpace(DeviceId) ?
            "" : $" --device-id {DeviceId}";
        DotNet($"build {MobileProject} -t:Install -f net10.0-android -c {Configuration}{deviceArg}");
    }

    void TryRestartAdbServer()
    {
        if (AdbTool is { } adb)
        {
            Log.Information("Restarting adb server...");

            adb("kill-server");
            adb("start-server");
        }
        else
        {
            Log.Warning("Could not find adb tool, skipping adb server restart. " +
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
