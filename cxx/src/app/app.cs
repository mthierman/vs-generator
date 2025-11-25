using System.CommandLine;
using System.Diagnostics;
using System.Reflection;

public static partial class App
{
    public enum ExitCode : int
    {
        Success = 0,
        GeneralError = 1,
    }

    public static string version { get; } = Assembly.GetExecutingAssembly()
              .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
              .InformationalVersion ?? string.Empty;
    public static string manifest_file = "cxx.jsonc";

    private static RootCommand root_command { get; } = new RootCommand($"cxx {version}");
    private static Argument<MSBuild.BuildConfiguration> build_configuration = new("build_configuration") { Arity = ArgumentArity.ZeroOrOne, Description = "Build Configuration (debug or release). Default: debug" };
    private static Dictionary<string, Command> sub_command = new Dictionary<string, Command>
    {
        ["new"] = new Command("new", "New project"),
        ["install"] = new Command("install", "Install project dependencies"),
        ["generate"] = new Command("generate", "Generate project build"),
        ["build"] = new Command("build", "Build project") { build_configuration },
        ["run"] = new Command("run", "Run project") { build_configuration },
        ["publish"] = new Command("publish", "Publish project"),
        ["clean"] = new Command("clean", "Clean project"),
        ["format"] = new Command("format", "Format project sources"),
    };

    static App()
    {
        foreach (var command in sub_command.Values)
        {
            root_command.Subcommands.Add(command);
        }

        sub_command["new"].SetAction(async parseResult =>
        {
            return await NewProject();
        });

        sub_command["install"].SetAction(async parseResult =>
        {
            return RunVcpkg("install");
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
            await MSBuild.Build(parseResult.GetValue(build_configuration));

            Process.Start(new ProcessStartInfo(Path.Combine(Paths.build, parseResult.GetValue(build_configuration) == MSBuild.BuildConfiguration.Debug ? "debug" : "release", "app.exe")))?.WaitForExit();

            return 0;
        });

        sub_command["publish"].SetAction(async parseResult =>
        {
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

    private static async Task<int> NewProject()
    {
        if (Directory.EnumerateFileSystemEntries(Environment.CurrentDirectory).Any())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Directory is not empty");
            Console.ResetColor();

            return 1;
        }

        var working_directory = Environment.CurrentDirectory;
        var blank_manifest_file = Path.Combine(working_directory, "cxx.jsonc");
        var vcpkg_manifest = Path.Combine(working_directory, "vcpkg.json");
        var vcpkg_configuration = Path.Combine(working_directory, "vcpkg-configuration.json");

        if (File.Exists(manifest_file) || File.Exists(vcpkg_manifest) || File.Exists(vcpkg_configuration))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Project already has a manifest file");
            Console.ResetColor();

            return 1;
        }

        await File.WriteAllTextAsync(blank_manifest_file, "{}");

        RunVcpkg("new", "--application");

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
    }

    public static int RunVcpkg(params string[] arguments)
    {
        var start_info = new ProcessStartInfo("vcpkg");

        foreach (var argument in arguments)
        {
            start_info.ArgumentList.Add(argument);
        }

        start_info.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
        start_info.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";

        Process.Start(start_info)?.WaitForExit();

        return 0;
    }

    public static int parse_args(string[] args)
    {
        return root_command.Parse(args).Invoke();
    }
}
