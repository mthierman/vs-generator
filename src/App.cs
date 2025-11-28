using System.CommandLine;
using System.Diagnostics;
using System.Reflection;

namespace cxx;

public static class App
{
    public static string Version { get; } = Assembly.GetExecutingAssembly()
              .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
              .InformationalVersion ?? "0.0.0";

    public static int Run(string[] args)
    {
        return RootCommand.Parse(args).Invoke();
    }

    private static readonly SemaphoreSlim ConsoleLock = new SemaphoreSlim(1, 1);
    private static RootCommand RootCommand { get; } = new RootCommand($"C++ build tool\nversion {Version}");
    private static Argument<MSBuild.BuildConfiguration> BuildConfiguration = new("BuildConfiguration") { Arity = ArgumentArity.ZeroOrOne, Description = "Build Configuration (debug or release). Default: debug" };

    private static Argument<string[]> VSWhereArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
    private static Argument<string[]> MSBuildArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
    private static Argument<string[]> NinjaArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
    private static Argument<string[]> VcpkgArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };

    private static Dictionary<string, Command> SubCommand = new Dictionary<string, Command>
    {
        ["vs"] = new Command("vs", "Visual Studio"),
        ["devenv"] = new Command("devenv", "Refresh developer environment"),
        ["vswhere"] = new Command("vswhere") { VSWhereArguments },
        ["msbuild"] = new Command("msbuild") { MSBuildArguments },
        ["ninja"] = new Command("ninja") { NinjaArguments },
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

    static App()
    {
        foreach (var command in SubCommand.Values)
        {
            RootCommand.Subcommands.Add(command);
        }

        SubCommand["vs"].SetAction(async parseResult =>
        {
            Console.WriteLine(VisualStudio.InstallPath);
            Console.WriteLine(VisualStudio.VSWherePath);
            Console.WriteLine(VisualStudio.MSBuildPath);
            Console.WriteLine(VisualStudio.ClPath);
            Console.WriteLine(VisualStudio.VcpkgPath);
        });

        SubCommand["devenv"].SetAction(async parseResult =>
        {
            var devEnv = await MSBuild.DevEnv;

            foreach (var kv in devEnv)
            {
                Console.WriteLine($"{kv.Key} = {kv.Value}");
            }

            Console.WriteLine();

            var sdk = await MSBuild.GetWindowsSdkExecutablePath();
            Console.WriteLine(sdk);
        });

        SubCommand["vswhere"].SetAction(async parseResult =>
        {
            var command = VisualStudio.VSWherePath;

            if (command is null || !File.Exists(command))
                return 1;

            return await ExternalCommand.Run(new(command), parseResult.GetValue(VSWhereArguments) ?? Array.Empty<string>());
        });

        SubCommand["msbuild"].SetAction(async parseResult =>
        {
            var command = VisualStudio.MSBuildPath;

            if (command is null || !File.Exists(command))
                return 1;

            return await ExternalCommand.Run(new(command), parseResult.GetValue(MSBuildArguments) ?? Array.Empty<string>());
        });

        SubCommand["ninja"].SetAction(async parseResult =>
        {
            var command = VisualStudio.NinjaPath;

            if (command is null || !File.Exists(command))
                return 1;

            return await ExternalCommand.Run(new(command), parseResult.GetValue(NinjaArguments) ?? Array.Empty<string>());
        });

        SubCommand["vcpkg"].SetAction(async parseResult =>
        {
            var command = VisualStudio.VcpkgPath;

            if (command is null || !File.Exists(command))
                return 1;

            return await ExternalCommand.Run(new(command), parseResult.GetValue(VcpkgArguments) ?? Array.Empty<string>());
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
