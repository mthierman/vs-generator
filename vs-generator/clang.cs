using System.Diagnostics;

public class Clang
{
    public static async Task FormatAsync()
    {
        var extensions = new[] { ".cpp", ".c", ".h", ".hpp", ".ixx" };
        var files = Directory.GetFiles(MSBuild.Paths.src_dir, "*.*", SearchOption.AllDirectories)
                             .Where(f => extensions.Contains(Path.GetExtension(f)))
                             .ToArray();

        if (files.Length == 0) return;

        var semaphore = new SemaphoreSlim(Environment.ProcessorCount);

        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync();
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "clang-format",
                        Arguments = $"-i \"{file}\"",
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrWhiteSpace(error))
                    Console.WriteLine($"Error formatting {file}: {error}");

                Console.WriteLine($"Formatted {file}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to format {file}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
    }
}
