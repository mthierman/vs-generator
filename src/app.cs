using System.CommandLine;
using System.Diagnostics;
using System.Reflection;

public static class App
{
    private static RootCommand root_command { get; } = new RootCommand($"C++ build tool\nversion {version}");
    private static Argument<MSBuild.BuildConfiguration> build_configuration = new("build_configuration") { Arity = ArgumentArity.ZeroOrOne, Description = "Build Configuration (debug or release). Default: debug" };
    private static Dictionary<string, Command> sub_command = new Dictionary<string, Command>
    {
        ["new"] = new Command("new", "New project"),
        ["install"] = new Command("install", "Install project dependencies"),
        ["generate"] = new Command("generate", "Generate project build"),
        ["build"] = new Command("build", "Build project") { build_configuration },
        ["run"] = new Command("run", "Run project") { build_configuration },
        ["publish"] = new Command("publish", "Publish project"),
        ["clean"] = new Command("clean", "Clean project"),
        ["format"] = new Command("format", "Format project sources"),
    };

    public static string version { get; } = Assembly.GetExecutingAssembly()
              .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
              .InformationalVersion ?? "0.0.0";
    public static string manifest_file = "cxx.jsonc";
    private static readonly Lazy<EnvironmentPaths> _environmentPaths = new Lazy<EnvironmentPaths>(InitializeEnvironmentPaths);
    public static EnvironmentPaths Paths => _environmentPaths.Value;

    public sealed record EnvironmentPaths(
        string root,
        string manifest,
        string vswhere,
        string msbuild,
        string vcpkg,
        string src,
        string build,
        string solution_file,
        string project_file
    );

    static App()
    {
        foreach (var command in sub_command.Values)
        {
            root_command.Subcommands.Add(command);
        }

        sub_command["new"].SetAction(async parseResult =>
        {
            return await NewProject();
        });

        sub_command["install"].SetAction(async parseResult =>
        {
            return RunVcpkg("install");
        });

        sub_command["generate"].SetAction(async parseResult =>
        {
            return await MSBuild.Generate();
        });

        sub_command["build"].SetAction(async parseResult =>
        {
            return await MSBuild.Build(parseResult.GetValue(build_configuration));
        });

        sub_command["run"].SetAction(async parseResult =>
        {
            await MSBuild.Build(parseResult.GetValue(build_configuration));

            Process.Start(new ProcessStartInfo(Path.Combine(Paths.build, parseResult.GetValue(build_configuration) == MSBuild.BuildConfiguration.Debug ? "debug" : "release", "app.exe")))?.WaitForExit();

            return 0;
        });

        sub_command["publish"].SetAction(async parseResult =>
        {
            return 0;
        });

        sub_command["clean"].SetAction(async parseResult =>
        {
            return MSBuild.Clean();
        });

        sub_command["format"].SetAction(async parseResult =>
        {
            await Clang.FormatAsync();

            return 0;
        });
    }

    public static int run(string[] args)
    {
        return root_command.Parse(args).Invoke();
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

        var working_directory = Environment.CurrentDirectory;
        var blank_manifest_file = Path.Combine(working_directory, "cxx.jsonc");
        var vcpkg_manifest = Path.Combine(working_directory, "vcpkg.json");
        var vcpkg_configuration = Path.Combine(working_directory, "vcpkg-configuration.json");

        if (File.Exists(manifest_file) || File.Exists(vcpkg_manifest) || File.Exists(vcpkg_configuration))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Project already has a manifest file");
            Console.ResetColor();

            return 1;
        }

        await File.WriteAllTextAsync(blank_manifest_file, "{}");

        RunVcpkg("new", "--application");

        if (!Directory.Exists(Paths.src))
            Directory.CreateDirectory(Paths.src);

        var app_cpp = Path.Combine(Paths.src, "app.cpp");

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
        var start_info = new ProcessStartInfo("vcpkg");

        foreach (var argument in arguments)
        {
            start_info.ArgumentList.Add(argument);
        }

        start_info.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
        start_info.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";

        Process.Start(start_info)?.WaitForExit();

        return 0;
    }

    private static EnvironmentPaths InitializeEnvironmentPaths()
    {
        // Find manifest & root
        var root = FindRepoRoot() ?? throw new FileNotFoundException($"{manifest_file} not found in any parent directory");
        var manifest = Path.Combine(root, manifest_file);

        // Find vswhere
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft Visual Studio\Installer\vswhere.exe");

        if (!File.Exists(vswhere))
            throw new FileNotFoundException($"vswhere.exe not found");

        // Find MSBuild
        var msbuild = FindMSBuild(vswhere) ?? throw new FileNotFoundException($"MSBuild.exe not found");

        // Find vcpkg
        var vcpkg_root = Environment.GetEnvironmentVariable("VCPKG_ROOT");

        if (string.IsNullOrWhiteSpace(vcpkg_root))
            throw new Exception("VCPKG_ROOT is not set");

        if (!Directory.Exists(vcpkg_root))
            throw new Exception($"VCPKG_ROOT not found: {vcpkg_root}");

        var vcpkg = Path.Combine(vcpkg_root, "vcpkg.exe");

        if (!File.Exists(vcpkg))
            throw new FileNotFoundException($"vcpkg.exe not found in VCPKG_ROOT: {vcpkg}");

        // Set src/build paths
        var src = Path.Combine(root, "src");
        var build = Path.Combine(root, "build");

        // Set VS paths
        var solution_file = Path.Combine(root, "build", "app.slnx");
        var project_file = Path.Combine(root, "build", "app.vcxproj");

        return new EnvironmentPaths(root, manifest, vswhere, msbuild, vcpkg, src, build, solution_file, project_file);
    }

    private static string? FindRepoRoot()
    {
        for (var current_directory = Environment.CurrentDirectory;
            !string.IsNullOrEmpty(current_directory);
            current_directory = Directory.GetParent(current_directory)?.FullName)
        {
            if (File.Exists(Path.Combine(current_directory, manifest_file)))
                return current_directory;
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
}
