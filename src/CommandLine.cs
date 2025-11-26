using System.CommandLine;
using System.Diagnostics;

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
        ["devshell"] = new Command("devshell", "Developer PowerShell"),
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

        SubCommand["devshell"].SetAction(async parseResult =>
        {
            var devShell = Find.DeveloperShell(Project.Tools.VSWhere);

            var startInfo = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-nol");
            startInfo.ArgumentList.Add("-nop");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"& '{devShell}' && [System.Environment]::GetEnvironmentVariables() | ConvertTo-Json");

            using var process = Process.Start(startInfo)!;

            _ = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                    {
                        lock (ConsoleLock)
                            Console.WriteLine(line);
                    }
                });

            _ = Task.Run(async () =>
                {
                    string? line;
                    while ((line = await process.StandardError.ReadLineAsync()) != null)
                    {
                        lock (ConsoleLock)
                            Console.Error.WriteLine(line);
                    }
                });

            await process.WaitForExitAsync();
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
