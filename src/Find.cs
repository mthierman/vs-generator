using System.Diagnostics;

namespace cxx;

public static class Find
{
    public static string? OnPath(string command)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(pathEnv))
            return null;

        string[] paths = pathEnv.Split(Path.PathSeparator);

        foreach (var dir in paths)
        {
            string fullPath = Path.Combine(dir, command);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    public static string? MSBuild()
    {
        if (string.IsNullOrEmpty(Project.Tools.VSWhere))
            return null;

        using var process = Process.Start(new ProcessStartInfo(Project.Tools.VSWhere,
            "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\amd64\\MSBuild.exe")
        {
            RedirectStandardOutput = true
        });

        if (process is null)
            return null;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var found = output
            .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(found) ? null : found;
    }

    public static string? Vcpkg()
    {
        var vcpkgRoot = Environment.GetEnvironmentVariable("VCPKG_ROOT");

        if (string.IsNullOrEmpty(vcpkgRoot))
            return OnPath("vcpkg.exe");

        var exe = Path.Combine(vcpkgRoot, "vcpkg.exe");
        return File.Exists(exe) ? exe : OnPath("vcpkg.exe");
    }
}
