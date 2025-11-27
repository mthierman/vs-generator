using System.Diagnostics;

namespace cxx;

public static class Vcpkg
{
    public static async Task<int> Run(params string[]? arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Project.Tools.Vcpkg,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = false
        };

        foreach (var argument in arguments ?? Array.Empty<string>())
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("Failed to start process.");

        await process.WaitForExitAsync();

        return process.ExitCode;
    }
}
