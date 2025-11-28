namespace cxx;

public static class Project
{
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
    }

    private static readonly Lazy<CorePaths> _corePaths = new Lazy<CorePaths>(InitCorePaths);
    private static readonly Lazy<ToolsPaths> _toolPaths = new Lazy<ToolsPaths>(InitToolsPaths);

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
            if (File.Exists(Path.Combine(cwd, Paths.Manifest.FileName)))
                root = cwd;

            cwd = Directory.GetParent(cwd)?.FullName;
        }

        if (string.IsNullOrEmpty(root))
            throw new FileNotFoundException($"No {Paths.Manifest.FileName}");

        return new(
            ProjectRoot: root,
            Manifest: Path.Combine(root, Paths.Manifest.FileName),
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
        var vcpkgManifest = Path.Combine(cwd, "vcpkg.json");
        var vcpkgConfig = Path.Combine(cwd, "vcpkg-configuration.json");

        if (File.Exists(Core.Manifest) || File.Exists(vcpkgManifest) || File.Exists(vcpkgConfig))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Project already has a manifest file");
            Console.ResetColor();

            return 1;
        }

        await File.WriteAllTextAsync(Path.Combine(cwd, Paths.Manifest.FileName), "{}");

        await VisualStudio.RunVcpkg("new", "--application");

        if (!Directory.Exists(Core.Src))
            Directory.CreateDirectory(Core.Src);

        var app_cpp = Path.Combine(Core.Src, "app.cpp");

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
