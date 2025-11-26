using System.Diagnostics;

namespace cxx;

public static class Project
{
    public static string ManifestFile = "cxx.jsonc";

    private static readonly Lazy<CorePaths> _corePaths =
        new Lazy<CorePaths>(() =>
        {
            var ProjectRoot = Find.ProjectRoot()
            ?? throw new FileNotFoundException($"No {ManifestFile}");

            return new(
                ProjectRoot: ProjectRoot,
                Manifest: Path.Combine(ProjectRoot, ManifestFile),
                Src: Path.Combine(ProjectRoot, "src"),
                Build: Path.Combine(ProjectRoot, "build"),
                SolutionFile: Path.Combine(ProjectRoot, "build", "app.slnx"),
                ProjectFile: Path.Combine(ProjectRoot, "build", "app.vcxproj")
            );
        });
    private static readonly Lazy<ToolsPaths> _toolPaths =
        new Lazy<ToolsPaths>(() =>
        {
            var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft Visual Studio\Installer\vswhere.exe");

            return new(
                VSWhere: vswhere,
                MSBuild: Find.MSBuild(),
                Vcpkg: Find.Vcpkg(),
                ClangFormat: Find.OnPath("clang-format.exe")
            );
        });

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
        string? VSWhere,
        string? MSBuild,
        string? Vcpkg,
        string? ClangFormat);

    public static class Find
    {
        public static string? ProjectRoot()
        {
            var cwd = Environment.CurrentDirectory;

            while (!string.IsNullOrEmpty(cwd))
            {
                if (File.Exists(Path.Combine(cwd, ManifestFile)))
                    return cwd;

                cwd = Directory.GetParent(cwd)?.FullName;
            }

            return null;
        }

        public static string? OnPath(string command)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");

            if (string.IsNullOrEmpty(pathEnv))
                return null;

            string[] paths = pathEnv.Split(Path.PathSeparator);

            foreach (var dir in paths)
            {
                string fullPath = Path.Combine(dir, command);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return null;
        }

        public static string? MSBuild()
        {
            using var process = Process.Start(new ProcessStartInfo(Tools.VSWhere!,
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

        public static string? Vcpkg()
        {
            var vcpkgRoot = Environment.GetEnvironmentVariable("VCPKG_ROOT");

            if (string.IsNullOrEmpty(vcpkgRoot))
                return OnPath("vcpkg.exe");

            var exe = Path.Combine(vcpkgRoot, "vcpkg.exe");
            return File.Exists(exe) ? exe : OnPath("vcpkg.exe");
        }
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

        var cwd = Environment.CurrentDirectory;
        var blankManifest = Path.Combine(cwd, "cxx.jsonc");
        var vcpkgManifest = Path.Combine(cwd, "vcpkg.json");
        var vcpkgConfig = Path.Combine(cwd, "vcpkg-configuration.json");

        if (File.Exists(Project.ManifestFile) || File.Exists(vcpkgManifest) || File.Exists(vcpkgConfig))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Project already has a manifest file");
            Console.ResetColor();

            return 1;
        }

        await File.WriteAllTextAsync(blankManifest, "{}");

        ExternalCommand.RunVcpkg("new", "--application");

        if (!Directory.Exists(Project.Core.Src))
            Directory.CreateDirectory(Project.Core.Src);

        var app_cpp = Path.Combine(Project.Core.Src, "app.cpp");

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
}
