using System.Diagnostics;

namespace cxx;

public static partial class App
{
    public static string ManifestFile = "cxx.jsonc";
    private static readonly Lazy<EnvironmentPaths> _environmentPaths = new Lazy<EnvironmentPaths>(InitializeEnvironmentPaths);
    public static EnvironmentPaths Paths => _environmentPaths.Value;
    public sealed record EnvironmentPaths(CorePaths Core, ToolsPaths Tools);
    public sealed record CorePaths
    {
        public required string Root { get; init; }
        public required string Manifest { get; init; }
        public required string Src { get; init; }
        public required string Build { get; init; }
        public required string SolutionFile { get; init; }
        public required string ProjectFile { get; init; }

        public bool HasRoot => Directory.Exists(Root);
        public bool HasManifest => File.Exists(Manifest);
        public bool HasSrc => Directory.Exists(Src);
        public bool HasBuild => Directory.Exists(Build);
        public bool HasSolutionFile => File.Exists(SolutionFile);
        public bool HasProjectFile => File.Exists(ProjectFile);
    }
    public sealed record ToolsPaths
    {
        public string? VSWhere { get; init; }
        public string? MSBuild { get; init; }
        public string? Vcpkg { get; init; }
        public string? ClangFormat { get; init; }

        public bool HasVSWhere => !string.IsNullOrEmpty(VSWhere);
        public bool HasMSBuild => !string.IsNullOrEmpty(MSBuild);
        public bool HasVcpkg => !string.IsNullOrEmpty(Vcpkg);
        public bool HasClangFormat => !string.IsNullOrEmpty(ClangFormat);
    }

    private static EnvironmentPaths InitializeEnvironmentPaths()
    {
        var root = Find.RepoRoot() ?? throw new FileNotFoundException($"{ManifestFile} not found in any parent directory");
        var manifest = Path.Combine(root, ManifestFile);
        var src = Path.Combine(root, "src");
        var build = Path.Combine(root, "build");
        var solutionFile = Path.Combine(build, "app.slnx");
        var projectFile = Path.Combine(build, "app.vcxproj");

        var corePaths = new CorePaths
        {
            Root = root,
            Manifest = manifest,
            Src = src,
            Build = build,
            SolutionFile = solutionFile,
            ProjectFile = projectFile
        };

        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft Visual Studio\Installer\vswhere.exe");

        var msbuild = Find.MSBuild(vswhere);

        var clangFormat = Find.OnPath("clang-format.exe");

        string? vcpkg = null;
        var vcpkgRoot = Environment.GetEnvironmentVariable("VCPKG_ROOT");
        if (!string.IsNullOrEmpty(vcpkgRoot))
        {
            var vcpkgExe = Path.Combine(vcpkgRoot, "vcpkg.exe");
            if (File.Exists(vcpkgExe))
                vcpkg = vcpkgExe;
            else
                vcpkg = Find.OnPath("vcpkg.exe");
        }

        var toolsPaths = new ToolsPaths
        {
            VSWhere = vswhere,
            MSBuild = msbuild,
            Vcpkg = vcpkg,
            ClangFormat = clangFormat
        };

        return new EnvironmentPaths(corePaths, toolsPaths);
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
            for (var cwd = Environment.CurrentDirectory;
                !string.IsNullOrEmpty(cwd);
                cwd = Directory.GetParent(cwd)?.FullName)
            {
                if (File.Exists(Path.Combine(cwd, ManifestFile)))
                    return cwd;
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
    }
}
