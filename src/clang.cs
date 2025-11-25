using System.Diagnostics;
using System.Text.Json;
using static App;

public class Clang
{
    public static async Task FormatAsync()
    {
        var extensions = new[] { ".cpp", ".c", ".h", ".hpp", ".ixx" };
        var files = Directory.GetFiles(Paths.src, "*.*", SearchOption.AllDirectories)
                             .Where(f => extensions.Contains(Path.GetExtension(f)))
                             .ToArray();

        Console.Out.WriteLine(JsonSerializer.Serialize(files, new JsonSerializerOptions { WriteIndented = true }));

        if (files.Length == 0) return;

        var semaphore = new SemaphoreSlim(Environment.ProcessorCount);

        Console.Error.WriteLine();
        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync();
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "clang-format",
                        Arguments = $"-i \"{file}\"",
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                    }
                };

                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error formatting {file}: {error}");
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Error.WriteLine($"✓ {file}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to format {file}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        Console.ResetColor();
    }
}
