namespace cxx;

public static class Project
{
    public static string ManifestFile = "cxx.jsonc";

    private static readonly Lazy<CorePaths> _corePaths =
        new Lazy<CorePaths>(() =>
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
                ProjectFile: Path.Combine(root, "build", "app.vcxproj")
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
