using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace CXX;

public static class App
{
    public static readonly string Name = "cxx";
    public static readonly string FileName = $"{Name}.exe";
    public static readonly string Version = Assembly.GetExecutingAssembly()
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
      .InformationalVersion ?? "0.0.0";
    public static ProcessStartInfo ProcessInfo => new() { FileName = FileName };
    public static VisualStudio.BuildConfiguration DefaultBuildConfiguration = VisualStudio.BuildConfiguration.Debug;

    public static class Config
    {
        public static string name = $"{Name}-project";
        public static string version = $"{Version}";
    }

    private static RootCommand RootCommand { get; } = new RootCommand($"C++ build tool\nversion {Version}");
    private static Argument<VisualStudio.BuildConfiguration> BuildConfiguration = new("BuildConfiguration") { Arity = ArgumentArity.ZeroOrOne, Description = "Build Configuration (debug or release). Default: debug" };
    private static Argument<string[]> VSWhereArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
    private static Argument<string[]> MSBuildArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
    private static Argument<string[]> NinjaArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
    private static Argument<string[]> VcpkgArguments = new Argument<string[]>("Args") { Arity = ArgumentArity.ZeroOrMore };
    private static Dictionary<string, Command> SubCommand = new Dictionary<string, Command>
    {
        ["new"] = new Command("new", "New project"),
        ["install"] = new Command("install", "Install project dependencies"),
        ["generate"] = new Command("generate", "Generate project build"),
        ["build"] = new Command("build", "Build project") { BuildConfiguration },
        ["run"] = new Command("run", "Run project") { BuildConfiguration },
        ["publish"] = new Command("publish", "Publish project"),
        ["format"] = new Command("format", "Format project sources"),
        ["clean"] = new Command("clean", "Clean project"),
        ["devenv"] = new Command("devenv", "Refresh developer environment"),
        ["vswhere"] = new Command("vswhere") { VSWhereArguments },
        ["msbuild"] = new Command("msbuild") { MSBuildArguments },
        ["ninja"] = new Command("ninja") { NinjaArguments },
        ["vcpkg"] = new Command("vcpkg") { VcpkgArguments },
    };

    static App()
    {
        foreach (var command in SubCommand.Values)
        {
            RootCommand.Subcommands.Add(command);
        }

        SubCommand["new"].SetAction(async parseResult =>
        {
            var manifestFile = Path.Combine(Environment.CurrentDirectory, Paths.ManifestFileName);

            if (Directory.EnumerateFileSystemEntries(Environment.CurrentDirectory).Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine($"Directory is not empty");
                Console.ResetColor();
                Console.Error.WriteLine();

                var processInfo = ProcessInfo;
                processInfo.ArgumentList.Add("--help");
                await Run(processInfo);

                return 1;
            }

            if (File.Exists(manifestFile))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine($"{manifestFile} exists");
                Console.ResetColor();
                Console.Error.WriteLine();

                return 1;
            }

            await File.WriteAllTextAsync(manifestFile, JsonSerializer.Serialize(new
            {
                Config.name,
                Config.version
            }, new JsonSerializerOptions
            {
                WriteIndented = true
            }));

            var startInfo = new ProcessStartInfo
            {
                FileName = VisualStudio.VcpkgPath
            };

            startInfo.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
            startInfo.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";

            await Run(startInfo, "new", "--application");

            await File.WriteAllTextAsync(Path.Combine(Directory.CreateDirectory(Paths.Project.Src).FullName, "app.cpp"), @"
                #include <print>

                auto wmain() -> int {
                    std::println(""Hello, World!"");

                    return 0;
                }
            ".Trim());

            return 0;
        });

        SubCommand["install"].SetAction(async parseResult =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = VisualStudio.VcpkgPath
            };

            startInfo.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
            startInfo.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";

            return await Run(startInfo, "install");
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

            Process.Start(new ProcessStartInfo(Path.Combine(Paths.Project.Build, parseResult.GetValue(BuildConfiguration) == VisualStudio.BuildConfiguration.Debug ? "debug" : "release", "app.exe")))?.WaitForExit();

            return 0;
        });

        SubCommand["publish"].SetAction(async parseResult =>
        {
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
    }

    public static int Start(string[] args)
    {
        return RootCommand.Parse(args).Invoke();
    }

    public static async Task<int> Run(ProcessStartInfo processStartInfo, params string[]? arguments)
    {
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

    public static class Paths
    {
        public static readonly string ManifestFileName = "cxx.jsonc";
        public static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        public static readonly string AppLocal = Path.Combine(Local, "cxx");
        public static readonly string AppRoaming = Path.Combine(Roaming, "cxx");

        public static ProjectPaths Project => _project.Value;
        private static readonly Lazy<ProjectPaths> _project = new(() =>
        {
            var cwd = Environment.CurrentDirectory;
            var root = string.Empty;

            while (!string.IsNullOrEmpty(cwd))
            {
                if (File.Exists(Path.Combine(cwd, ManifestFileName)))
                    root = cwd;

                cwd = Directory.GetParent(cwd)?.FullName;
            }

            if (string.IsNullOrEmpty(root))
                throw new FileNotFoundException($"Manifest not found: {ManifestFileName}");

            return new(
                Root: root,
                Manifest: Path.Combine(root, ManifestFileName),
                Src: Path.Combine(root, "src"),
                Build: Path.Combine(root, "build"),
                SolutionFile: Path.Combine(root, "build", "app.slnx"),
                ProjectFile: Path.Combine(root, "build", "app.vcxproj"));
        });

        public sealed record ProjectPaths(
            string Root,
            string Manifest,
            string Src,
            string Build,
            string SolutionFile,
            string ProjectFile);
    }
}
