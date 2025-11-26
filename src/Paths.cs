using System.Diagnostics;

namespace cxx;

public static partial class App
{
    public static string ManifestFile = "cxx.jsonc";
    private static readonly Lazy<EnvironmentPaths> _paths =
        new Lazy<EnvironmentPaths>(InitializeEnvironmentPaths);
    public static EnvironmentPaths Paths => _paths.Value;
    public sealed record EnvironmentPaths(CorePaths Core, ToolsPaths Tools);
    public sealed record CorePaths(
        string Root,
        string Manifest,
        string Src,
        string Build,
        string SolutionFile,
        string ProjectFile)
    {
        public bool HasRoot => Directory.Exists(Root);
        public bool HasManifest => File.Exists(Manifest);
        public bool HasSrc => Directory.Exists(Src);
        public bool HasBuild => Directory.Exists(Build);
        public bool HasSolutionFile => File.Exists(SolutionFile);
        public bool HasProjectFile => File.Exists(ProjectFile);
    }

    public sealed record ToolsPaths(
        string? VSWhere,
        string? MSBuild,
        string? Vcpkg,
        string? ClangFormat)
    {
        public bool HasVSWhere => !string.IsNullOrEmpty(VSWhere);
        public bool HasMSBuild => !string.IsNullOrEmpty(MSBuild);
        public bool HasVcpkg => !string.IsNullOrEmpty(Vcpkg);
        public bool HasClangFormat => !string.IsNullOrEmpty(ClangFormat);
    }

    private static EnvironmentPaths InitializeEnvironmentPaths()
    {
        var root = Find.RepoRoot()
            ?? throw new FileNotFoundException($"{ManifestFile} not found in any parent directory");

        var core = new CorePaths(
            Root: root,
            Manifest: Path.Combine(root, ManifestFile),
            Src: Path.Combine(root, "src"),
            Build: Path.Combine(root, "build"),
            SolutionFile: Path.Combine(root, "build", "app.slnx"),
            ProjectFile: Path.Combine(root, "build", "app.vcxproj")
        );

        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft Visual Studio\Installer\vswhere.exe");

        var tools = new ToolsPaths(
            VSWhere: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft Visual Studio\Installer\vswhere.exe"),
            MSBuild: Find.MSBuild(vswhere),
            Vcpkg: Find.Vcpkg(),
            ClangFormat: Find.OnPath("clang-format.exe")
        );

        return new EnvironmentPaths(core, tools);
    }

    public static class Find
    {
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

        public static string? RepoRoot()
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

        public static string? MSBuild(string vswhere)
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

        public static string? Vcpkg()
        {
            var vcpkgRoot = Environment.GetEnvironmentVariable("VCPKG_ROOT");

            if (string.IsNullOrEmpty(vcpkgRoot))
                return OnPath("vcpkg.exe");

            var exe = Path.Combine(vcpkgRoot, "vcpkg.exe");
            return File.Exists(exe) ? exe : OnPath("vcpkg.exe");
        }
    }
}
