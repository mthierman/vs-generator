using Microsoft.VisualStudio.Setup.Configuration;

public static class VisualStudio
{
    private static readonly Lazy<ISetupInstance?> _latest = new(GetLatestInstance);

    /// <summary>The latest installed Visual Studio instance (any edition, including Build Tools).</summary>
    public static ISetupInstance? Latest => _latest.Value;

    /// <summary>The installation path of the latest Visual Studio instance, or null if none installed.</summary>
    public static string? InstallPath => Latest?.GetInstallationPath();

    /// <summary>The vswhere path of the latest Visual Studio instance (64-bit).</summary>
    public static string? VSWherePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Microsoft Visual Studio", "Installer", "vswhere.exe");

    /// <summary>The MSBuild path of the latest Visual Studio instance (64-bit).</summary>
    public static string? MSBuildPath => InstallPath is string path
        ? Path.Combine(path, "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe")
        : null;

    /// <summary>The ninja path of the latest Visual Studio instance (64-bit).</summary>
    public static string? NinjaPath => InstallPath is string path
        ? Path.Combine(path, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "Ninja", "ninja.exe")
        : null;

    /// <summary>The vcpkg path of the latest Visual Studio instance (64-bit).</summary>
    public static string? VcpkgPath => InstallPath is string path
        ? Path.Combine(path, "VC", "vcpkg", "vcpkg.exe")
        : null;

    /// <summary>The path to cl.exe compiler of the latest Visual Studio instance (x64).</summary>
    public static string? ClPath => LatestMSVCVersionPath() is string path
        ? Path.Combine(path, "bin", "Hostx64", "x64", "cl.exe")
        : null;

    /// <summary>Finds the latest MSVC version folder inside VC\Tools\MSVC.</summary>
    private static string? LatestMSVCVersionPath()
    {
        if (InstallPath is null) return null;

        var vcRoot = Path.Combine(InstallPath, "VC", "Tools", "MSVC");
        if (!Directory.Exists(vcRoot)) return null;

        return Directory.GetDirectories(vcRoot)
            .OrderByDescending(Path.GetFileName)
            .FirstOrDefault();
    }

    /// <summary>Finds the latest installed Visual Studio instance.</summary>
    private static ISetupInstance? GetLatestInstance()
    {
        var setupConfig = new SetupConfiguration();
        var enumInstances = setupConfig.EnumAllInstances();

        ISetupInstance? latest = null;
        ISetupInstance[] buffer = new ISetupInstance[1];
        int fetched;

        do
        {
            enumInstances.Next(1, buffer, out fetched);
            if (fetched > 0)
            {
                var instance = buffer[0];
                if (latest == null || string.Compare(instance.GetInstallationVersion(),
                    latest.GetInstallationVersion(), StringComparison.Ordinal) > 0)
                {
                    latest = instance;
                }
            }
        } while (fetched > 0);

        return latest;
    }

    public static async Task<int> PrintInstances()
    {
        var setupConfig = new SetupConfiguration();
        var enumInstances = setupConfig.EnumAllInstances(); // returns IEnumSetupInstances

        ISetupInstance[] instances = new ISetupInstance[1];
        int fetched = 0;

        ISetupInstance? latestInstance = null;

        // Loop until Next returns 0 items
        do
        {
            enumInstances.Next(1, instances, out fetched); // returns void, fetched tells how many items
            if (fetched == 0) break;

            var instance = instances[0];
            if (latestInstance == null ||
                string.Compare(instance.GetInstallationVersion(), latestInstance.GetInstallationVersion(), StringComparison.Ordinal) > 0)
            {
                latestInstance = instance;
            }

        } while (fetched > 0);

        if (latestInstance != null)
        {
            Console.WriteLine($"Latest VS install path: {latestInstance.GetInstallationPath()}");
        }

        return 0;
    }
}
