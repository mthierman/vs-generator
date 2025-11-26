using System.Diagnostics;

namespace cxx;

public static partial class App
{
    public static async Task<int> RunProcess(string? command, string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = string.Join(" ", args),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        return 0;
    }

    public static int RunVcpkg(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("vcpkg");

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
        startInfo.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";

        Process.Start(startInfo)?.WaitForExit();

        return 0;
    }
}
