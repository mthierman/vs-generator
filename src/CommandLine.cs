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
    private static Argument<string[]> NinjaBuildArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
    private static Argument<string[]> VcpkgArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
    private static Dictionary<string, Command> SubCommand = new Dictionary<string, Command>
    {
        ["devenv"] = new Command("devenv", "Refresh developer environment"),
        ["msbuild"] = new Command("msbuild") { MSBuildArguments },
        ["ninja"] = new Command("ninja") { NinjaBuildArguments },
        ["vcpkg"] = new Command("vcpkg") { VcpkgArguments },
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
            var devEnv = await MSBuild.DevEnv;

            foreach (var kv in devEnv)
            {
                Console.WriteLine($"{kv.Key} = {kv.Value}");
            }

            return await ExternalCommand.Run(await MSBuild.DevEnvironmentTools.MSBuild(), "-version");
        });

        SubCommand["msbuild"].SetAction(async parseResult =>
        {
            var args = parseResult.GetValue(MSBuildArguments) ?? Array.Empty<string>();

            return await ExternalCommand.Run(Project.Tools.MSBuild, args);
        });

        SubCommand["ninja"].SetAction(async parseResult =>
        {
            var args = parseResult.GetValue(NinjaBuildArguments) ?? Array.Empty<string>();

            return await ExternalCommand.Run("ninja.exe", args);
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
            return await ExternalCommand.RunVcpkg("install");
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
