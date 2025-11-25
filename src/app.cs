using System.CommandLine;
using System.Diagnostics;
using System.Reflection;

public static class App
{
    public static string Version { get; } = Assembly.GetExecutingAssembly()
              .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
              .InformationalVersion ?? "0.0.0";
    public static string ManifestFile = "cxx.jsonc";
    private static readonly Lazy<EnvironmentPaths> _environmentPaths = new Lazy<EnvironmentPaths>(InitializeEnvironmentPaths);
    public static EnvironmentPaths Paths => _environmentPaths.Value;
    public sealed record EnvironmentPaths(
        string Root,
        string Manifest,
        string? Vswhere,
        string? MSBuild,
        string? Vcpkg,
        string? ClangFormat,
        string Src,
        string Build,
        string SolutionFile,
        string ProjectFile
    );

    private static EnvironmentPaths InitializeEnvironmentPaths()
    {
        // Find manifest & root
        var root = FindRepoRoot() ?? throw new FileNotFoundException($"{ManifestFile} not found in any parent directory");
        var manifest = Path.Combine(root, ManifestFile);

        // Find vswhere
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft Visual Studio\Installer\vswhere.exe");

        // Find MSBuild
        var msbuild = FindMSBuild(vswhere) ?? string.Empty;

        // Find clang-format
        var clangFormat = FindClangFormat() ?? string.Empty;

        // Find vcpkg
        var vcpkgRoot = Environment.GetEnvironmentVariable("VCPKG_ROOT");
        var vcpkg = string.Empty;

        if (vcpkgRoot != null)
        {
            if (File.Exists(Path.Combine(vcpkgRoot, "vcpkg.exe")))
                vcpkg = Path.Combine(vcpkgRoot, "vcpkg.exe");
        }

        // Set src/build paths
        var src = Path.Combine(root, "src");
        var build = Path.Combine(root, "build");

        // Set VS paths
        var solutionFile = Path.Combine(root, "build", "app.slnx");
        var projectFile = Path.Combine(root, "build", "app.vcxproj");

        return new EnvironmentPaths(root, manifest, vswhere, msbuild, vcpkg, clangFormat, src, build, solutionFile, projectFile);
    }

    private static RootCommand RootCommand { get; } = new RootCommand($"C++ build tool\nversion {Version}");
    private static Argument<MSBuild.BuildConfiguration> BuildConfiguration = new("build_configuration") { Arity = ArgumentArity.ZeroOrOne, Description = "Build Configuration (debug or release). Default: debug" };
    private static Dictionary<string, Command> SubCommand = new Dictionary<string, Command>
    {
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

        SubCommand["new"].SetAction(async parseResult =>
        {
            return await NewProject();
        });

        SubCommand["install"].SetAction(async parseResult =>
        {
            return RunVcpkg("install");
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

            Process.Start(new ProcessStartInfo(Path.Combine(Paths.Build, parseResult.GetValue(BuildConfiguration) == MSBuild.BuildConfiguration.Debug ? "debug" : "release", "app.exe")))?.WaitForExit();

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

    public static int Run(string[] args)
    {
        return RootCommand.Parse(args).Invoke();
    }

    private static async Task<int> NewProject()
    {
        if (Directory.EnumerateFileSystemEntries(Environment.CurrentDirectory).Any())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Directory is not empty");
            Console.ResetColor();

            return 1;
        }

        var cwd = Environment.CurrentDirectory;
        var blankManifest = Path.Combine(cwd, "cxx.jsonc");
        var vcpkgManifest = Path.Combine(cwd, "vcpkg.json");
        var vcpkgConfig = Path.Combine(cwd, "vcpkg-configuration.json");

        if (File.Exists(ManifestFile) || File.Exists(vcpkgManifest) || File.Exists(vcpkgConfig))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Project already has a manifest file");
            Console.ResetColor();

            return 1;
        }

        await File.WriteAllTextAsync(blankManifest, "{}");

        RunVcpkg("new", "--application");

        if (!Directory.Exists(Paths.Src))
            Directory.CreateDirectory(Paths.Src);

        var app_cpp = Path.Combine(Paths.Src, "app.cpp");

        if (!File.Exists(app_cpp))
        {
            await File.WriteAllTextAsync(app_cpp, @"
#include <print>

auto wmain() -> int {
    std::println(""Hello, World!"");

    return 0;
}
".Trim());
        }

        return 0;
    }

    public static int RunVcpkg(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("vcpkg");

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
        startInfo.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";

        Process.Start(startInfo)?.WaitForExit();

        return 0;
    }

    private static string? FindRepoRoot()
    {
        for (var cwd = Environment.CurrentDirectory;
            !string.IsNullOrEmpty(cwd);
            cwd = Directory.GetParent(cwd)?.FullName)
        {
            if (File.Exists(Path.Combine(cwd, ManifestFile)))
                return cwd;
        }

        return null;
    }

    private static string? FindMSBuild(string vswhere)
    {
        using var process = Process.Start(new ProcessStartInfo(vswhere,
            "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\amd64\\MSBuild.exe")
        {
            RedirectStandardOutput = true
        });

        if (process is null)
            return null;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var found = output
            .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(found) ? null : found;
    }

    public static string? FindClangFormat()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(pathEnv))
            return null;

        string[] paths = pathEnv.Split(Path.PathSeparator);

        foreach (var dir in paths)
        {
            string fullPath = Path.Combine(dir, "clang-format.exe");
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }
}
