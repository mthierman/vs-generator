using System.CommandLine;
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
    private static Dictionary<string, Command> sub_command = new Dictionary<string, Command>
    {
        ["new"] = new Command("new", "Scaffold project"),
        ["install"] = new Command("install", "Install dependencies"),
        ["generate"] = new Command("generate", "Generate build"),
        ["debug"] = new Command("debug", "Build debug"),
        ["release"] = new Command("release", "Build release"),
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
            VCPkg.Start("new --application");

            if (!Directory.Exists(MSBuild.Paths.src_dir))
                Directory.CreateDirectory(MSBuild.Paths.src_dir);

            var app_cpp = Path.Combine(MSBuild.Paths.src_dir, "app.cpp");

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

        sub_command["debug"].SetAction(async parseResult =>
        {
            return await MSBuild.Build(MSBuild.BuildConfiguration.Debug) ? 0 : 1;
        });

        sub_command["release"].SetAction(async parseResult =>
        {
            return await MSBuild.Build(MSBuild.BuildConfiguration.Release) ? 0 : 1;
        });

        sub_command["clean"].SetAction(async parseResult =>
        {
            return MSBuild.Clean() ? 0 : 1;
        });

        sub_command["run"].SetAction(async parseResult =>
        {
            Process.Start(new ProcessStartInfo(Path.Combine(MSBuild.Paths.base_dir, "build", "debug", "app.exe")) { WorkingDirectory = MSBuild.Paths.base_dir })?.WaitForExit();

            return 0;
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
