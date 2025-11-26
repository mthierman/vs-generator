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

    public static string DeveloperShell(string vswhere)
    {
        if (!File.Exists(vswhere))
            throw new FileNotFoundException($"vswhere.exe not found");

        using var process = Process.Start(new ProcessStartInfo(vswhere,
            "-latest -products * -property installationPath")
        {
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("vswhere.exe failed to start");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var installPath = output
            .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
            .First()
            .Trim();

        var launchVsDevShell = Path.Combine(installPath,
                                                    "Common7",
                                                    "Tools",
                                                    "Launch-VsDevShell.ps1");

        if (!File.Exists(launchVsDevShell))
            throw new FileNotFoundException("Launch-VsDevShell.ps1 not found", launchVsDevShell);

        return launchVsDevShell;
    }

    public static string DeveloperPrompt(string vswhere)
    {
        if (!File.Exists(vswhere))
            throw new FileNotFoundException($"vswhere.exe not found");

        using var process = Process.Start(new ProcessStartInfo(vswhere,
            "-latest -products * -property installationPath")
        {
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("vswhere.exe failed to start");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var installPath = output
            .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
            .First()
            .Trim();

        var launchVsDevPrompt = Path.Combine(installPath,
                                                    "Common7",
                                                    "Tools",
                                                    "VsDevCmd.bat");

        if (!File.Exists(launchVsDevPrompt))
            throw new FileNotFoundException("VsDevCmd.bat not found", launchVsDevPrompt);

        return launchVsDevPrompt;
    }

    public static string MSBuild(string vswhere)
    {
        if (!File.Exists(vswhere))
            throw new FileNotFoundException($"vswhere.exe not found");

        using var process = Process.Start(new ProcessStartInfo(vswhere,
            "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\amd64\\MSBuild.exe")
        {
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("vswhere.exe failed to start"); ;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var msbuild = output
            .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .FirstOrDefault();

        if (!File.Exists(msbuild))
            throw new FileNotFoundException($"MSBuild.exe not found");

        return output;
    }

    public static string Vcpkg()
    {
        var vcpkgRoot = Environment.GetEnvironmentVariable("VCPKG_ROOT");

        if (string.IsNullOrEmpty(vcpkgRoot))
            throw new FileNotFoundException($"VCPKG_ROOT isn't set");

        var vcpkg = Path.Combine(vcpkgRoot, "vcpkg.exe");

        if (!File.Exists(vcpkg))
            throw new FileNotFoundException($"vcpkg.exe not found");

        return vcpkg;
    }

    public static string ClangFormat()
    {
        var clangFormat = OnPath("clang-format.exe");

        if (!File.Exists(clangFormat))
            throw new FileNotFoundException($"clang-format.exe not found");

        return clangFormat;
    }
}
