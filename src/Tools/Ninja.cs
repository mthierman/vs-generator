using System.Diagnostics;

namespace cxx;

public static class Ninja
{
    public static async Task<int> Run(params string[]? args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = VisualStudio.NinjaPath,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = false
        };

        return await App.Run(startInfo, args!);
    }
}
