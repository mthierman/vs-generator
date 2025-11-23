using System.Diagnostics;

public class Clang
{
    public static async Task format()
    {
        var extensions = new[] { ".cpp", ".c", ".h", ".hpp", ".ixx" };
        var files = Directory.GetFiles(MSBuild.Paths.src_dir, "*.*", SearchOption.AllDirectories)
                             .Where(f => extensions.Contains(Path.GetExtension(f)))
                             .ToArray();

        if (files.Length == 0)
        {
            return;
        }

        var tasks = files.Select(file => Task.Run(async () =>
        {
            var process = new Process();
            process.StartInfo.FileName = "clang-format";
            process.StartInfo.Arguments = $"-i \"{file}\"";
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;

            process.Start();

            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.WriteLine($"Error formatting {file}: {error}");
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Console.WriteLine("📐Formatting complete");
    }
}
