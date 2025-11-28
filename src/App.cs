using System.CommandLine;
using System.Diagnostics;
using System.Reflection;

namespace cxx;

public static class App
{
    public static string Version { get; } = Assembly.GetExecutingAssembly()
              .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
              .InformationalVersion ?? "0.0.0";

    public static class Paths
    {
        public static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        public static readonly string AppLocal = Path.Combine(Local, "cxx");
        public static readonly string AppRoaming = Path.Combine(Roaming, "cxx");

        public static class Manifest
        {
            public static string FileName = "cxx.jsonc";
            public static string FullPath = Path.Combine(AppLocal, "cxx.jsonc");
        }

        private static readonly Lazy<CorePaths> _corePaths = new(InitCorePaths);
        private static readonly Lazy<ToolsPaths> _toolPaths = new(InitToolsPaths);

        public static CorePaths Core => _corePaths.Value;
        public static ToolsPaths Tools => _toolPaths.Value;

        public sealed record CorePaths(
            string ProjectRoot,
            string Manifest,
            string Src,
            string Build,
            string SolutionFile,
            string ProjectFile);

        public sealed record ToolsPaths(
            string VSWhere,
            string MSBuild,
            string Vcpkg,
            string ClangFormat);

        private static CorePaths InitCorePaths()
        {
            var cwd = Environment.CurrentDirectory;
            var root = string.Empty;

            while (!string.IsNullOrEmpty(cwd))
            {
                if (File.Exists(Path.Combine(cwd, Manifest.FileName)))
                    root = cwd;

                cwd = Directory.GetParent(cwd)?.FullName;
            }

            if (string.IsNullOrEmpty(root))
                throw new FileNotFoundException($"No {Manifest.FileName}");

            return new(
                ProjectRoot: root,
                Manifest: Path.Combine(root, Manifest.FileName),
                Src: Path.Combine(root, "src"),
                Build: Path.Combine(root, "build"),
                SolutionFile: Path.Combine(root, "build", "app.slnx"),
                ProjectFile: Path.Combine(root, "build", "app.vcxproj"));
        }

        private static ToolsPaths InitToolsPaths()
        {
            var vswhere = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                            @"Microsoft Visual Studio\Installer\vswhere.exe");

            return new(
                VSWhere: Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                            @"Microsoft Visual Studio\Installer\vswhere.exe"),
                MSBuild: Find.MSBuild(vswhere),
                Vcpkg: Find.Vcpkg(),
                ClangFormat: Find.ClangFormat()
            );
        }
    }

    public static int Start(string[] args)
    {
        return RootCommand.Parse(args).Invoke();
    }

    public static async Task<int> Run(ProcessStartInfo processStartInfo, params string[]? arguments)
    {
        if (processStartInfo.FileName is null || !File.Exists(processStartInfo.FileName))
            return 1;

        processStartInfo.UseShellExecute = false;
        processStartInfo.RedirectStandardOutput = false;
        processStartInfo.RedirectStandardError = false;
        processStartInfo.CreateNoWindow = false;

        foreach (var argument in arguments ?? Array.Empty<string>())
            processStartInfo.ArgumentList.Add(argument);

        using var process = Process.Start(processStartInfo)
                      ?? throw new InvalidOperationException($"Failed to start process: {processStartInfo.FileName}.");

        await process.WaitForExitAsync();

        return process.ExitCode;
    }

    // private static readonly SemaphoreSlim ConsoleLock = new SemaphoreSlim(1, 1);
    private static RootCommand RootCommand { get; } = new RootCommand($"C++ build tool\nversion {Version}");
    private static Argument<VisualStudio.BuildConfiguration> BuildConfiguration = new("BuildConfiguration") { Arity = ArgumentArity.ZeroOrOne, Description = "Build Configuration (debug or release). Default: debug" };

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
            var devEnv = await VisualStudio.DevEnv;

            foreach (var kv in devEnv)
            {
                Console.WriteLine($"{kv.Key} = {kv.Value}");
            }

            Console.WriteLine();

            var sdk = await VisualStudio.GetWindowsSdkExecutablePath();
            Console.WriteLine(sdk);
        });

        SubCommand["vswhere"].SetAction(async parseResult =>
        {
            return await Run(new(VisualStudio.VSWherePath), parseResult.GetValue(VSWhereArguments));
        });

        SubCommand["msbuild"].SetAction(async parseResult =>
        {
            if (VisualStudio.MSBuildPath is null)
                return 1;

            return await Run(new(VisualStudio.MSBuildPath), parseResult.GetValue(MSBuildArguments));
        });

        SubCommand["ninja"].SetAction(async parseResult =>
        {
            if (VisualStudio.NinjaPath is null)
                return 1;

            return await Run(new(VisualStudio.NinjaPath), parseResult.GetValue(NinjaArguments));
        });

        SubCommand["vcpkg"].SetAction(async parseResult =>
        {
            if (VisualStudio.VcpkgPath is null)
                return 1;

            return await Run(new(VisualStudio.VcpkgPath), parseResult.GetValue(VcpkgArguments));
        });

        SubCommand["new"].SetAction(async parseResult =>
        {
            return await Project.New();
        });

        SubCommand["install"].SetAction(async parseResult =>
        {
            return await VisualStudio.RunVcpkg("install");
        });

        SubCommand["generate"].SetAction(async parseResult =>
        {
            return await VisualStudio.Generate();
        });

        SubCommand["build"].SetAction(async parseResult =>
        {
            return await VisualStudio.Build(parseResult.GetValue(BuildConfiguration));
        });

        SubCommand["run"].SetAction(async parseResult =>
        {
            await VisualStudio.Build(parseResult.GetValue(BuildConfiguration));

            Process.Start(new ProcessStartInfo(Path.Combine(Paths.Core.Build, parseResult.GetValue(BuildConfiguration) == VisualStudio.BuildConfiguration.Debug ? "debug" : "release", "app.exe")))?.WaitForExit();

            return 0;
        });

        SubCommand["publish"].SetAction(async parseResult =>
        {
            return 0;
        });

        SubCommand["clean"].SetAction(async parseResult =>
        {
            return VisualStudio.Clean();
        });

        SubCommand["format"].SetAction(async parseResult =>
        {
            await Clang.FormatAsync();

            return 0;
        });
    }
}
