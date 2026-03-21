using System.CommandLine;
using System.Diagnostics;
using System.Reflection;

namespace CXX;

public static class App
{
    public static class MetaData
    {
        public static readonly string Name = "cxx";
        public static readonly string FileName = $"{Name}.exe";
        public static readonly string Version = Assembly.GetExecutingAssembly()
          .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
          .InformationalVersion ?? "0.0.0";
    }

    public static int Main(string[] args)
    {
        return Commands.Root.Parse(args).Invoke();
    }

    public static class Commands
    {
        public static RootCommand Root = new($"C++ build tool\nversion {MetaData.Version}");
        private static Argument<Project.BuildConfiguration> Config = new("Config") { Arity = ArgumentArity.ZeroOrOne, Description = "Build Configuration (debug or release). Default: debug" };
        private static Argument<string[]> VSWhereArgs = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
        private static Argument<string[]> MSBuildArgs = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
        private static Argument<string[]> CLArgs = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
        private static Argument<string[]> RCArgs = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
        private static Argument<string[]> NinjaArgs = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
        private static Argument<string[]> NugetArgs = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
        private static Argument<string[]> VcpkgArgs = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
        private static Dictionary<string, Command> SubCommand = new Dictionary<string, Command>
        {
            ["new"] = new Command("new", "New project"),
            ["install"] = new Command("install", "Install project dependencies"),
            ["generate"] = new Command("generate", "Generate project build"),
            ["build"] = new Command("build", "Build project") { Config },
            ["run"] = new Command("run", "Run project") { Config },
            ["publish"] = new Command("publish", "Publish project"),
            ["format"] = new Command("format", "Format project sources"),
            ["clean"] = new Command("clean", "Clean project"),
            ["devenv"] = new Command("devenv", "Refresh developer environment"),
            ["vswhere"] = new Command("vswhere") { VSWhereArgs },
            ["msbuild"] = new Command("msbuild") { MSBuildArgs },
            ["cl"] = new Command("cl") { CLArgs },
            ["rc"] = new Command("rc") { RCArgs },
            ["ninja"] = new Command("ninja") { NinjaArgs },
            ["nuget"] = new Command("nuget") { NugetArgs },
            ["vcpkg"] = new Command("vcpkg") { VcpkgArgs },
        };

        static Commands()
        {
            foreach (var command in SubCommand.Values)
            {
                Root.Subcommands.Add(command);
            }

            SubCommand["new"].SetAction(async parseResult =>
            {
                return await Project.New();
            });

            SubCommand["install"].SetAction(async parseResult =>
            {
                return await InstallVcpkg();
            });

            SubCommand["generate"].SetAction(async parseResult =>
            {
                var exitCode = await VisualStudio.Generate();

                if (exitCode != 0)
                    return exitCode;

                return exitCode;
            });

            SubCommand["build"].SetAction(async parseResult =>
            {
                return await VisualStudio.Build(parseResult.GetValue(Config));
            });

            SubCommand["run"].SetAction(async parseResult =>
            {
                var exitCode = await VisualStudio.Build(parseResult.GetValue(Config));

                if (exitCode != 0)
                    return exitCode;

                return await Run(new(parseResult.GetValue(Config) == Project.BuildConfiguration.Debug ? Project.Exe.Debug.FileName : Project.Exe.Release.FileName));
            });

            SubCommand["publish"].SetAction(async parseResult =>
            {
                if (!Directory.Exists(Project.Paths.Publish))
                    Directory.CreateDirectory(Project.Paths.Publish);

                var build = await VisualStudio.Build(Project.BuildConfiguration.Release);

                var destination = Path.Combine(Project.Paths.Publish, MetaData.FileName);

                if (File.Exists(destination))
                    File.Delete(destination);

                File.Copy(Project.Exe.Release.FileName, destination);

                Print.Err($"File ({MetaData.FileName}) copied: {destination}", ConsoleColor.Green);

                return 0;
            });

            SubCommand["format"].SetAction(async parseResult =>
            {
                await VisualStudio.FormatAsync();

                return 0;
            });

            SubCommand["clean"].SetAction(async parseResult =>
            {
                return VisualStudio.Clean();
            });

            SubCommand["devenv"].SetAction(async parseResult =>
            {
                var devEnv = await VisualStudio.DevEnv;

                foreach (var kv in devEnv)
                {
                    Console.WriteLine($"{kv.Key} = {kv.Value}");
                }
            });

            SubCommand["vswhere"].SetAction(async parseResult =>
            {
                return await Run(new(VisualStudio.VSWherePath), parseResult.GetValue(VSWhereArgs));
            });

            SubCommand["msbuild"].SetAction(async parseResult =>
            {
                if (VisualStudio.MSBuildPath is null)
                    return 1;

                return await Run(new(VisualStudio.MSBuildPath), parseResult.GetValue(MSBuildArgs));
            });

            SubCommand["cl"].SetAction(async parseResult =>
            {
                if (VisualStudio.ClPath is null)
                    return 1;

                return await Run(new(VisualStudio.ClPath), parseResult.GetValue(MSBuildArgs));
            });

            SubCommand["rc"].SetAction(async parseResult =>
            {
                if (VisualStudio.RcPath is null)
                    return 1;

                return await Run(new(VisualStudio.RcPath), parseResult.GetValue(MSBuildArgs));
            });

            SubCommand["ninja"].SetAction(async parseResult =>
            {
                if (VisualStudio.NinjaPath is null)
                    return 1;

                return await Run(new(VisualStudio.NinjaPath), parseResult.GetValue(NinjaArgs));
            });

            SubCommand["nuget"].SetAction(async parseResult =>
            {
                if (VisualStudio.NugetPath is null)
                    return 1;

                return await Run(new(VisualStudio.NugetPath), parseResult.GetValue(NugetArgs));
            });

            SubCommand["vcpkg"].SetAction(async parseResult =>
            {
                if (VisualStudio.VcpkgPath is null)
                    return 1;

                return await Run(new(VisualStudio.VcpkgPath), parseResult.GetValue(VcpkgArgs));
            });
        }
    }

    public static async Task<int> PrintHelp()
    {
        Print.Err();
        return await Commands.Root.Parse("--help").InvokeAsync();
    }

    public static async Task<int> InstallVcpkg()
    {
        var processInfo = Project.Exe.Vcpkg;
        processInfo.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
        processInfo.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";
        return await Run(processInfo, "install");
    }

    public static async Task<int> Run(ProcessStartInfo processStartInfo, params string[]? arguments)
    {
        processStartInfo.UseShellExecute = false;
        processStartInfo.RedirectStandardOutput = false;
        processStartInfo.RedirectStandardError = false;
        processStartInfo.CreateNoWindow = false;

        foreach (var argument in arguments ?? Array.Empty<string>())
            processStartInfo.ArgumentList.Add(argument);

        Console.Error.WriteLine($"{processStartInfo.FileName}");

        using var process = Process.Start(processStartInfo)
                      ?? throw new InvalidOperationException($"Failed to start process: {processStartInfo.FileName}.");

        await process.WaitForExitAsync();

        return process.ExitCode;
    }
}
