using Microsoft.VisualStudio.Setup.Configuration;

public static class VisualStudio
{
    private static readonly Lazy<ISetupInstance?> _latest = new(GetLatestInstance);

    /// <summary>The latest installed Visual Studio instance (any edition, including Build Tools).</summary>
    public static ISetupInstance? Latest => _latest.Value;

    /// <summary>The installation path of the latest Visual Studio instance, or null if none installed.</summary>
    public static string? InstallPath => Latest?.GetInstallationPath();

    /// <summary>The MSBuild path of the latest Visual Studio instance (64-bit).</summary>
    public static string? MSBuildPath => InstallPath is string path
        ? Path.Combine(path, "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe")
        : null;

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
}
