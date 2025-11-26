using System.Diagnostics;

namespace cxx;

public static class Clang
{
    private static readonly SemaphoreSlim ConsoleLock = new SemaphoreSlim(1, 1);

    private static async Task FormatFileAsync(string file, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "clang-format",
                Arguments = $"-i \"{file}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        try
        {
            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var error = await stderrTask;

            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new Exception(error.Trim());
            }
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            throw;
        }
    }

    public static async Task FormatAsync(CancellationToken ct = default)
    {
        var extensions = new[] { ".c", ".cpp", ".cxx", ".h", ".hpp", ".hxx", ".ixx" };

        var files = Directory.GetFiles(Project.Core.Src, "*.*", SearchOption.AllDirectories)
                             .Where(f => extensions.Contains(Path.GetExtension(f)))
                             .ToArray();

        if (files.Length == 0)
            return;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        };

        try
        {
            await Parallel.ForEachAsync(files, options, async (file, token) =>
            {
                try
                {
                    await FormatFileAsync(file, token);

                    await ConsoleLock.WaitAsync(token);

                    try
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✓ {file}");
                    }
                    finally
                    {
                        Console.ResetColor();
                        ConsoleLock.Release();
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await ConsoleLock.WaitAsync(token);

                    try
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Error.WriteLine($"Error formatting {file}: {ex.Message}");
                    }
                    finally
                    {
                        Console.ResetColor();
                        ConsoleLock.Release();
                    }
                }
            });
        }
        finally
        {
            Console.ResetColor();
        }
    }

    public static void Generate()
    {
        // ----- 16. Generate compile_commands.json -----
        //var compileCommands = new List<Dictionary<string, string>>();

        //foreach (var config in configurations)
        //{
        //    foreach (var platform in platforms)
        //    {
        //        string includeFlags = $"/I$(ProjectDir)"; // Add more include dirs as needed
        //        string defines = preprocessor;            // from your ClCompile metadata
        //        string languageStandard = "/std:c++23";

        //        var allSourceFiles = Directory.GetFiles(src_dir, "*.*")
        //                                      .Where(f => f.EndsWith(".cpp") || f.EndsWith(".ixx"));

        //        foreach (var file in allSourceFiles)
        //        {
        //            var relativePath = Path.GetRelativePath(build_dir, file).Replace('\\', '/');

        //            string command = $"cl.exe /c {languageStandard} {includeFlags} /D{defines} \"{relativePath}\"";

        //            compileCommands.Add(new Dictionary<string, string>
        //            {
        //                ["directory"] = Path.GetFullPath(build_dir),
        //                ["command"] = command,
        //                ["file"] = Path.GetFullPath(file)
        //            });
        //        }
        //    }
        //}

        //var options = new JsonSerializerOptions { WriteIndented = true };
        //string json = JsonSerializer.Serialize(compileCommands, options);

        //File.WriteAllText(Path.Combine(build_dir, "compile_commands.json"), json);
        //Console.WriteLine("compile_commands.json generated.");
    }
}
