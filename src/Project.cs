using System.Diagnostics;

namespace cxx;

public static class Project
{
    public static string ManifestFile = "cxx.jsonc";

    private static readonly Lazy<CorePaths> _corePaths = new Lazy<CorePaths>(InitCorePaths());
    private static readonly Lazy<ToolsPaths> _toolPaths = new Lazy<ToolsPaths>(InitToolsPaths());
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
            if (File.Exists(Path.Combine(cwd, ManifestFile)))
                root = cwd;

            cwd = Directory.GetParent(cwd)?.FullName;
        }

        if (string.IsNullOrEmpty(root))
            throw new FileNotFoundException($"No {ManifestFile}");

        return new(
            ProjectRoot: root,
            Manifest: Path.Combine(root, ManifestFile),
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
            ClangFormat: Find.OnPath("clang-format.exe")
        );
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

        public static string MSBuild(string vswhere)
        {
            if (!File.Exists(vswhere))
                throw new FileNotFoundException($"vswhere.exe not found");

            using var process = Process.Start(new ProcessStartInfo(vswhere,
                "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\amd64\\MSBuild.exe")
            {
                RedirectStandardOutput = true
            });

            if (process is null)
                throw new InvalidOperationException($"vswhere.exe not found");

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var msbuild = output
                .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .FirstOrDefault();

            if (!File.Exists(msbuild))
                throw new FileNotFoundException($"MSBuild.exe not found");

            return output;
        }

        public static string Vcpkg()
        {
            var vcpkgRoot = Environment.GetEnvironmentVariable("VCPKG_ROOT");

            if (string.IsNullOrEmpty(vcpkgRoot))
                throw new FileNotFoundException($"VCPKG_ROOT isn't set");

            var vcpkg = Path.Combine(vcpkgRoot, "vcpkg.exe");

            if (!File.Exists(vcpkg))
                throw new FileNotFoundException($"vcpkg.exe not found");

            return vcpkg;
        }
    }

}
