using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;

namespace cxx;

public static class CommandLine
{
    private static readonly SemaphoreSlim ConsoleLock = new SemaphoreSlim(1, 1);

    public static RootCommand RootCommand { get; } = new RootCommand($"C++ build tool\nversion {App.Version}");
    private static Argument<MSBuild.BuildConfiguration> BuildConfiguration = new("BuildConfiguration") { Arity = ArgumentArity.ZeroOrOne, Description = "Build Configuration (debug or release). Default: debug" };
    private static Argument<string[]> MSBuildArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
    private static Argument<string[]> VcpkgArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
    private static Dictionary<string, Command> SubCommand = new Dictionary<string, Command>
    {
        ["devenv"] = new Command("devenv", "Refresh developer environment"),
        ["devenv_print"] = new Command("devenv_print", "Print developer environment"),
        ["devenv_msbuild"] = new Command("devenv_msbuild", "Run MSBuild from developer environment"),
        ["msbuild"] = new Command("msbuild", "MSBuild command") { MSBuildArguments },
        ["vcpkg"] = new Command("vcpkg", "vcpkg command") { VcpkgArguments },
        ["new"] = new Command("new", "New project"),
        ["install"] = new Command("install", "Install project dependencies"),
        ["generate"] = new Command("generate", "Generate project build"),
        ["build"] = new Command("build", "Build project") { BuildConfiguration },
        ["run"] = new Command("run", "Run project") { BuildConfiguration },
        ["publish"] = new Command("publish", "Publish project"),
        ["clean"] = new Command("clean", "Clean project"),
        ["format"] = new Command("format", "Format project sources"),
    };

    static CommandLine()
    {
        foreach (var command in SubCommand.Values)
        {
            RootCommand.Subcommands.Add(command);
        }

        SubCommand["devenv"].SetAction(async parseResult =>
        {
            return await MSBuild.RefreshDevEnv();
        });

        SubCommand["devenv_print"].SetAction(async parseResult =>
        {
            if (!File.Exists(Project.SystemFolders.DevEnvJson))
                await MSBuild.RefreshDevEnv();

            var json = File.ReadAllText(Project.SystemFolders.DevEnvJson);
            MSBuild.DevEnv = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                     ?? throw new InvalidOperationException("Failed to parse DevShell environment JSON.");

            foreach (var kv in MSBuild.DevEnv!)
            {
                Console.WriteLine($"{kv.Key} = {kv.Value}");
            }
        });

        SubCommand["devenv_msbuild"].SetAction(async parseResult =>
        {
            var startInfo = await MSBuild.DevEnvProcessStartInfo("msbuild");

            using var process = Process.Start(startInfo)
                       ?? throw new InvalidOperationException("Failed to start MSBuild.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            Console.WriteLine(await stdoutTask);
            Console.Error.WriteLine(await stderrTask);
        });

        SubCommand["msbuild"].SetAction(async parseResult =>
        {
            var args = parseResult.GetValue(MSBuildArguments) ?? Array.Empty<string>();

            return await ExternalCommand.Run(Project.Tools.MSBuild, args);
        });

        SubCommand["vcpkg"].SetAction(async parseResult =>
        {
            var args = parseResult.GetValue(VcpkgArguments) ?? Array.Empty<string>();

            return await ExternalCommand.Run(Project.Tools.Vcpkg, args);
        });

        SubCommand["new"].SetAction(async parseResult =>
        {
            return await Project.New();
        });

        SubCommand["install"].SetAction(async parseResult =>
        {
            return ExternalCommand.RunVcpkg("install");
        });

        SubCommand["generate"].SetAction(async parseResult =>
        {
            return await MSBuild.Generate();
        });

        SubCommand["build"].SetAction(async parseResult =>
        {
            return await MSBuild.Build(parseResult.GetValue(BuildConfiguration));
        });

        SubCommand["run"].SetAction(async parseResult =>
        {
            await MSBuild.Build(parseResult.GetValue(BuildConfiguration));

            Process.Start(new ProcessStartInfo(Path.Combine(Project.Core.Build, parseResult.GetValue(BuildConfiguration) == MSBuild.BuildConfiguration.Debug ? "debug" : "release", "app.exe")))?.WaitForExit();

            return 0;
        });

        SubCommand["publish"].SetAction(async parseResult =>
        {
            return 0;
        });

        SubCommand["clean"].SetAction(async parseResult =>
        {
            return MSBuild.Clean();
        });

        SubCommand["format"].SetAction(async parseResult =>
        {
            await Clang.FormatAsync();

            return 0;
        });
    }
}
