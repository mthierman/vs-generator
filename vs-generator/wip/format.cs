using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0 || (args[0] != "fmt" && args[0] != "format"))
        {
            Console.WriteLine("Please provide a command: fmt or format");
            return;
        }

        await RunClangFormatAsync();
    }

    static async Task format()
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

    static async Task RunClangFormatAsync()
    {
        var srcDir = Path.Combine(Environment.CurrentDirectory, "src");

        var files = Directory.GetFiles(srcDir, "*.*", SearchOption.AllDirectories)
                             .Where(f => f.EndsWith(".cpp") || f.EndsWith(".h") ||
                                         f.EndsWith(".c") || f.EndsWith(".hpp"))
                             .ToArray();

        if (files.Length == 0)
        {
            Console.WriteLine("No source files found in src folder.");
            return;
        }

        // Create a task for each file
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

        // Run all tasks in parallel
        await Task.WhenAll(tasks);

        Console.WriteLine("Formatting complete!");
    }
}
