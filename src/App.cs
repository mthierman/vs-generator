using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace CXX;

public static class App
{
    public static int Main(string[] args)
    {
        return Commands.Root.Parse(args).Invoke();
    }

    public static class MetaData
    {
        public static readonly string Name = "cxx";
        public static readonly string FileName = $"{Name}.exe";
        public static readonly string Version = Assembly.GetExecutingAssembly()
          .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
          .InformationalVersion ?? "0.0.0";
    }

    public sealed class ProjectConfig
    {
        public static string name { get; set; } = $"{MetaData.Name}-project";
        public static string version { get; set; } = "0.0.0";
    }

    public static class ConfigManager
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            AllowTrailingCommas = true // optional
        };

        public static ProjectConfig Load()
        {
            if (!File.Exists(Paths.Project.Manifest))
            {
                var cfg = new ProjectConfig();
                Save(cfg);
                return cfg;
            }

            var json = File.ReadAllText(Paths.Project.Manifest);
            return JsonSerializer.Deserialize<ProjectConfig>(json, Options)
                   ?? new ProjectConfig();
        }

        public static void Save(ProjectConfig config)
        {
            var json = JsonSerializer.Serialize(config, Options);
            File.WriteAllText(Paths.Project.Manifest, json);
        }
    }

    public enum BuildConfiguration
    {
        Debug,
        Release
    }

    public static class Exe
    {
        public static ProcessStartInfo Debug => new() { FileName = Path.Combine(Paths.Project.Build, "debug", "app.exe") };
        public static ProcessStartInfo Release => new() { FileName = Path.Combine(Paths.Project.Build, "release", "app.exe") };
        public static ProcessStartInfo CXX => new() { FileName = Environment.ProcessPath };
        public static ProcessStartInfo VSWhere => new() { FileName = VisualStudio.VSWherePath };
        public static ProcessStartInfo MSBuild => new() { FileName = VisualStudio.MSBuildPath };
        public static ProcessStartInfo CL => new() { FileName = VisualStudio.ClPath };
        public static ProcessStartInfo RC => new() { FileName = VisualStudio.RcPath };
        public static ProcessStartInfo Vcpkg => new() { FileName = VisualStudio.VcpkgPath };
        public static ProcessStartInfo Ninja => new() { FileName = VisualStudio.NinjaPath };
        public static ProcessStartInfo ClangFormat => new() { FileName = VisualStudio.ClangFormatPath };
    }

    public static class Commands
    {
        public static RootCommand Root = new($"C++ build tool\nversion {MetaData.Version}");
        private static Argument<BuildConfiguration> Config = new("Config") { Arity = ArgumentArity.ZeroOrOne, Description = "Build Configuration (debug or release). Default: debug" };
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
                return await NewProject();
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

                return await Run(new(parseResult.GetValue(Config) == BuildConfiguration.Debug ? Exe.Debug.FileName : Exe.Release.FileName));
            });

            SubCommand["publish"].SetAction(async parseResult =>
            {
                if (!Directory.Exists(Paths.Project.Publish))
                    Directory.CreateDirectory(Paths.Project.Publish);

                var build = await VisualStudio.Build(BuildConfiguration.Release);

                var destination = Path.Combine(Paths.Project.Publish, MetaData.FileName);

                if (File.Exists(destination))
                    File.Delete(destination);

                File.Copy(Exe.Release.FileName, destination);

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

    public static async Task<int> NewProject()
    {
        // var manifestFile = Path.Combine(Environment.CurrentDirectory, Paths.ManifestFileName);

        if (Directory.EnumerateFileSystemEntries(Environment.CurrentDirectory).Any() || File.Exists(Paths.Project.Manifest))
        {
            Print.Err($"Directory was not empty.", ConsoleColor.Red);
            await PrintHelp();

            return 1;
        }

        var json = JsonSerializer.Serialize(new
        {
            ProjectConfig.name,
            ProjectConfig.version
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        Print.Err($"Generated new {MetaData.Name} project", ConsoleColor.Green);
        Console.Error.WriteLine();
        Print.Err(json, ConsoleColor.DarkGreen);

        await File.WriteAllTextAsync(Paths.Project.Manifest, json);

        var processInfo = Exe.Vcpkg;
        processInfo.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
        processInfo.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";
        await Run(processInfo, "new", "--application");

        await File.WriteAllTextAsync(Path.Combine(Directory.CreateDirectory(Paths.Project.Src).FullName, "app.cpp"), @"
                #include <print>

                auto wmain() -> int {
                    std::println(""Hello, World!"");

                    return 0;
                }
            ".Trim());

        return 0;
    }

    public static async Task<int> PrintHelp()
    {
        Print.Err();
        return await Commands.Root.Parse("--help").InvokeAsync();
    }

    public static class Print
    {
        public static void Out()
        {
            Console.ResetColor();
            Console.Out.WriteLine();
        }

        public static void Out(string message)
        {
            Console.Out.WriteLine(message);
            Console.ResetColor();
        }

        public static void Out(string message, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Out.WriteLine(message);
            Console.ResetColor();
        }

        public static void Err()
        {
            Console.ResetColor();
            Console.Error.WriteLine();
        }

        public static void Err(string message)
        {
            Console.Error.WriteLine(message);
            Console.ResetColor();
        }

        public static void Err(string message, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Error.WriteLine(message);
            Console.ResetColor();
        }
    }

    public static async Task<int> InstallVcpkg()
    {
        var processInfo = Exe.Vcpkg;
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

    public static class Paths
    {
        public static readonly string ManifestFileName = "cxx.jsonc";
        public static readonly string AppLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cxx");
        public static readonly string AppRoaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cxx");

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
                Debug: Path.Combine(root, "build", "debug"),
                Release: Path.Combine(root, "build", "release"),
                Publish: Path.Combine(root, "build", "publish"),
                SolutionFile: Path.Combine(root, "build", "app.slnx"),
                ProjectFile: Path.Combine(root, "build", "app.vcxproj"));
        });

        public sealed record ProjectPaths(
            string Root,
            string Manifest,
            string Src,
            string Build,
            string Debug,
            string Release,
            string Publish,
            string SolutionFile,
            string ProjectFile);
    }
}
