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
            var process_start_info = new ProcessStartInfo
            {
                FileName = "vcpkg",
                Arguments = "new --application"
            };

            process_start_info.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
            process_start_info.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";

            Process.Start(process_start_info)?.WaitForExit();

            return 0;
        });

        sub_command["install"].SetAction(async parseResult =>
        {
            var process_start_info = new ProcessStartInfo
            {
                FileName = "vcpkg",
                Arguments = "install"
            };

            process_start_info.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
            process_start_info.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";

            Process.Start(process_start_info)?.WaitForExit();
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
            using var process = Process.Start(new ProcessStartInfo() { FileName = Path.Combine(MSBuild.Paths.base_dir, "build", "debug", "app.exe"), WorkingDirectory = MSBuild.Paths.base_dir });
            process?.WaitForExit();

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
