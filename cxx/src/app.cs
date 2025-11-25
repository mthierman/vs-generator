using System.CommandLine;
// using System.CommandLine.Completions;
// using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Reflection;

public class App
{
    public enum ExitCode : int
    {
        Success = 0,
        GeneralError = 1,
    }

    public static string version { get; } = Assembly.GetExecutingAssembly()
              .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
              .InformationalVersion ?? string.Empty;

    private static RootCommand root_command { get; } = new RootCommand($"vs-generator {version}");
    private static Argument<MSBuild.BuildConfiguration> build_configuration = new("build_configuration") { Arity = ArgumentArity.ZeroOrOne, Description = "Build Configuration (debug or release). Default: debug" };
    private static Dictionary<string, Command> sub_command = new Dictionary<string, Command>
    {
        ["new"] = new Command("new", "Scaffold project"),
        ["install"] = new Command("install", "Install dependencies"),
        ["generate"] = new Command("generate", "Generate build"),
        ["build"] = new Command("build", "Build") { build_configuration },
        ["clean"] = new Command("clean", "Clean build"),
        ["run"] = new Command("run", "Run build"),
        ["format"] = new Command("format", "Format sources"),
    };

    static App()
    {
        foreach (var command in sub_command.Values)
        {
            root_command.Subcommands.Add(command);
        }

        sub_command["new"].SetAction(async parseResult =>
        {
            if (Directory.EnumerateFileSystemEntries(Environment.CurrentDirectory).Any())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Directory is not empty");
                Console.ResetColor();

                return 1;
            }

            var working_directory = Environment.CurrentDirectory;
            var manifest_file = Path.Combine(working_directory, "cv.jsonc");
            var vcpkg_manifest = Path.Combine(working_directory, "vcpkg.json");
            var vcpkg_configuration = Path.Combine(working_directory, "vcpkg-configuration.json");

            if (File.Exists(manifest_file) || File.Exists(vcpkg_manifest) || File.Exists(vcpkg_configuration))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Project already has a manifest file");
                Console.ResetColor();

                return 1;
            }

            await File.WriteAllTextAsync(manifest_file, "{}");

            VCPkg.Start("new --application");

            if (!Directory.Exists(Paths.src))
                Directory.CreateDirectory(Paths.src);

            var app_cpp = Path.Combine(Paths.src, "app.cpp");

            if (!File.Exists(app_cpp))
            {
                await File.WriteAllTextAsync(app_cpp, @"
#include <print>

auto wmain() -> int {
    std::println(""Hello, World!"");

    return 0;
}
".Trim());
            }

            return 0;
        });

        sub_command["install"].SetAction(async parseResult =>
        {
            VCPkg.Start("install");
        });

        sub_command["generate"].SetAction(async parseResult =>
        {
            return (await MSBuild.Generate()) ? 0 : 1;
        });

        sub_command["build"].SetAction(async parseResult =>
        {
            return await MSBuild.Build(parseResult.GetValue(build_configuration)) ? 0 : 1;
        });

        sub_command["run"].SetAction(async parseResult =>
        {
            await MSBuild.Build(MSBuild.BuildConfiguration.Debug);

            Process.Start(new ProcessStartInfo(Path.Combine(Paths.build, "debug", "app.exe")))?.WaitForExit();

            return 0;
        });

        sub_command["clean"].SetAction(async parseResult =>
        {
            return MSBuild.Clean() ? 0 : 1;
        });

        sub_command["format"].SetAction(async parseResult =>
        {
            await Clang.FormatAsync();

            return 0;
        });
    }

    public static int parse_args(string[] args)
    {
        return root_command.Parse(args).Invoke();
    }
}
