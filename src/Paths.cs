using System.Diagnostics;
using System.Reflection;

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
        var root = FindRepoRoot() ?? throw new FileNotFoundException($"{ManifestFile} not found in any parent directory");
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

        var msbuild = FindMSBuild(vswhere);

        var clangFormat = FindOnPath("clang-format.exe");

        string? vcpkg = null;
        var vcpkgRoot = Environment.GetEnvironmentVariable("VCPKG_ROOT");
        if (!string.IsNullOrEmpty(vcpkgRoot))
        {
            var vcpkgExe = Path.Combine(vcpkgRoot, "vcpkg.exe");
            if (File.Exists(vcpkgExe))
                vcpkg = vcpkgExe;
            else
                vcpkg = FindOnPath("vcpkg.exe");
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

    //     public static async Task<int> RunProcess(string? command, string[] args)
    //     {
    //         var startInfo = new ProcessStartInfo
    //         {
    //             FileName = command,
    //             Arguments = string.Join(" ", args),
    //             RedirectStandardOutput = true,
    //             RedirectStandardError = true,
    //             UseShellExecute = false
    //         };

    //         using var process = new Process { StartInfo = startInfo };
    //         process.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
    //         process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

    //         process.Start();
    //         process.BeginOutputReadLine();
    //         process.BeginErrorReadLine();

    //         await process.WaitForExitAsync();

    //         return 0;
    //     }

    //     public static async Task<int> NewProject()
    //     {
    //         if (Directory.EnumerateFileSystemEntries(Environment.CurrentDirectory).Any())
    //         {
    //             Console.ForegroundColor = ConsoleColor.Red;
    //             Console.Error.WriteLine("Directory is not empty");
    //             Console.ResetColor();

    //             return 1;
    //         }

    //         var cwd = Environment.CurrentDirectory;
    //         var blankManifest = Path.Combine(cwd, "cxx.jsonc");
    //         var vcpkgManifest = Path.Combine(cwd, "vcpkg.json");
    //         var vcpkgConfig = Path.Combine(cwd, "vcpkg-configuration.json");

    //         if (File.Exists(ManifestFile) || File.Exists(vcpkgManifest) || File.Exists(vcpkgConfig))
    //         {
    //             Console.ForegroundColor = ConsoleColor.Red;
    //             Console.Error.WriteLine("Project already has a manifest file");
    //             Console.ResetColor();

    //             return 1;
    //         }

    //         await File.WriteAllTextAsync(blankManifest, "{}");

    //         RunVcpkg("new", "--application");

    //         if (!Directory.Exists(Paths.Core.Src))
    //             Directory.CreateDirectory(Paths.Core.Src);

    //         var app_cpp = Path.Combine(Paths.Core.Src, "app.cpp");

    //         if (!File.Exists(app_cpp))
    //         {
    //             await File.WriteAllTextAsync(app_cpp, @"
    // #include <print>

    // auto wmain() -> int {
    //     std::println(""Hello, World!"");

    //     return 0;
    // }
    // ".Trim());
    //         }

    //         return 0;
    //     }

    //     public static int RunVcpkg(params string[] arguments)
    //     {
    //         var startInfo = new ProcessStartInfo("vcpkg");

    //         foreach (var argument in arguments)
    //         {
    //             startInfo.ArgumentList.Add(argument);
    //         }

    //         startInfo.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
    //         startInfo.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";

    //         Process.Start(startInfo)?.WaitForExit();

    //         return 0;
    //     }

    //     private static string? FindOnPath(string command)
    //     {
    //         var pathEnv = Environment.GetEnvironmentVariable("PATH");

    //         if (string.IsNullOrEmpty(pathEnv))
    //             return null;

    //         string[] paths = pathEnv.Split(Path.PathSeparator);

    //         foreach (var dir in paths)
    //         {
    //             string fullPath = Path.Combine(dir, command);
    //             if (File.Exists(fullPath))
    //                 return fullPath;
    //         }

    //         return null;
    //     }

    //     private static string? FindRepoRoot()
    //     {
    //         for (var cwd = Environment.CurrentDirectory;
    //             !string.IsNullOrEmpty(cwd);
    //             cwd = Directory.GetParent(cwd)?.FullName)
    //         {
    //             if (File.Exists(Path.Combine(cwd, ManifestFile)))
    //                 return cwd;
    //         }

    //         return null;
    //     }

    //     private static string? FindMSBuild(string vswhere)
    //     {
    //         using var process = Process.Start(new ProcessStartInfo(vswhere,
    //             "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\amd64\\MSBuild.exe")
    //         {
    //             RedirectStandardOutput = true
    //         });

    //         if (process is null)
    //             return null;

    //         var output = process.StandardOutput.ReadToEnd();
    //         process.WaitForExit();

    //         var found = output
    //             .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
    //             .Select(s => s.Trim())
    //             .FirstOrDefault();

    //         return string.IsNullOrWhiteSpace(found) ? null : found;
    //     }
}
