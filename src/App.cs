using System.CommandLine;
using System.Diagnostics;
using System.Reflection;

namespace CXX;

public static class App
{
    public static string Version { get; } = Assembly.GetExecutingAssembly()
              .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
              .InformationalVersion ?? "0.0.0";
    public static readonly string ManifestFileName = "cxx.jsonc";

    public static class Paths
    {
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

    public static async Task<int> New()
    {
        if (Directory.EnumerateFileSystemEntries(Environment.CurrentDirectory).Any())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Directory is not empty");
            Console.ResetColor();

            return 1;
        }

        await File.WriteAllTextAsync(Path.Combine(Environment.CurrentDirectory, ManifestFileName), "{}");

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

            // foreach (var kv in devEnv)
            // {
            //     Console.WriteLine($"{kv.Key} = {kv.Value}");
            // }

            // Console.WriteLine();

            // var sdk = await VisualStudio.GetWindowsSdkExecutablePath();
            // Console.WriteLine(sdk);
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
            return await New();
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

        SubCommand["clean"].SetAction(async parseResult =>
        {
            return VisualStudio.Clean();
        });

        SubCommand["format"].SetAction(async parseResult =>
        {
            await VisualStudio.FormatAsync();

            return 0;
        });
    }

    public static class Find
    {
        // public static string? OnPath(string command)
        // {
        //     var pathEnv = Environment.GetEnvironmentVariable("PATH");

        //     if (string.IsNullOrEmpty(pathEnv))
        //         return null;

        //     string[] paths = pathEnv.Split(Path.PathSeparator);

        //     foreach (var dir in paths)
        //     {
        //         string fullPath = Path.Combine(dir, command);
        //         if (File.Exists(fullPath))
        //             return fullPath;
        //     }

        //     return null;
        // }

        // public static string DeveloperShell(string vswhere)
        // {
        //     if (!File.Exists(vswhere))
        //         throw new FileNotFoundException($"vswhere.exe not found");

        //     using var process = Process.Start(new ProcessStartInfo(vswhere,
        //         "-latest -products * -property installationPath")
        //     {
        //         RedirectStandardOutput = true
        //     }) ?? throw new InvalidOperationException("vswhere.exe failed to start");

        //     var output = process.StandardOutput.ReadToEnd();
        //     process.WaitForExit();

        //     var installPath = output
        //         .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
        //         .First()
        //         .Trim();

        //     var launchVsDevShell = Path.Combine(installPath,
        //                                                 "Common7",
        //                                                 "Tools",
        //                                                 "Launch-VsDevShell.ps1");

        //     if (!File.Exists(launchVsDevShell))
        //         throw new FileNotFoundException("Launch-VsDevShell.ps1 not found", launchVsDevShell);

        //     return launchVsDevShell;
        // }

        // https://learn.microsoft.com/en-us/visualstudio/ide/reference/command-prompt-powershell?view=visualstudio
        // public static string DeveloperPrompt(string vswhere)
        // {
        //     if (!File.Exists(vswhere))
        //         throw new FileNotFoundException($"vswhere.exe not found");

        //     using var process = Process.Start(new ProcessStartInfo(vswhere,
        //         "-latest -products * -property installationPath")
        //     {
        //         RedirectStandardOutput = true
        //     }) ?? throw new InvalidOperationException("vswhere.exe failed to start");

        //     var output = process.StandardOutput.ReadToEnd();
        //     process.WaitForExit();

        //     var installPath = output
        //         .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
        //         .First()
        //         .Trim();

        //     var launchVsDevPrompt = Path.Combine(installPath,
        //                                                 "Common7",
        //                                                 "Tools",
        //                                                 "VsDevCmd.bat");

        //     if (!File.Exists(launchVsDevPrompt))
        //         throw new FileNotFoundException("VsDevCmd.bat not found", launchVsDevPrompt);

        //     return launchVsDevPrompt;
        // }

        // public static string MSBuild(string vswhere)
        // {
        //     if (!File.Exists(vswhere))
        //         throw new FileNotFoundException($"vswhere.exe not found");

        //     using var process = Process.Start(new ProcessStartInfo(vswhere,
        //         "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\amd64\\MSBuild.exe")
        //     {
        //         RedirectStandardOutput = true
        //     }) ?? throw new InvalidOperationException("vswhere.exe failed to start"); ;

        //     var output = process.StandardOutput.ReadToEnd();
        //     process.WaitForExit();

        //     var msbuild = output
        //         .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
        //         .Select(s => s.Trim())
        //         .FirstOrDefault();

        //     if (!File.Exists(msbuild))
        //         throw new FileNotFoundException($"MSBuild.exe not found");

        //     return output;
        // }

        // public static string Vcpkg()
        // {
        //     var vcpkgRoot = Environment.GetEnvironmentVariable("VCPKG_ROOT");

        //     if (string.IsNullOrEmpty(vcpkgRoot))
        //         throw new FileNotFoundException($"VCPKG_ROOT isn't set");

        //     var vcpkg = Path.Combine(vcpkgRoot, "vcpkg.exe");

        //     if (!File.Exists(vcpkg))
        //         throw new FileNotFoundException($"vcpkg.exe not found");

        //     return vcpkg;
        // }

        // public static string ClangFormat()
        // {
        //     var clangFormat = OnPath("clang-format.exe");

        //     if (!File.Exists(clangFormat))
        //         throw new FileNotFoundException($"clang-format.exe not found");

        //     return clangFormat;
        // }
    }
}
