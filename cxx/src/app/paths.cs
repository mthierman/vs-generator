using System.Diagnostics;

public partial class App
{
    public static string manifest_file = "cxx.jsonc";
    private static readonly Lazy<EnvironmentPaths> _environmentPaths = new Lazy<EnvironmentPaths>(InitializeEnvironmentPaths);
    public static EnvironmentPaths Paths = _environmentPaths.Value;

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

    private static EnvironmentPaths InitializeEnvironmentPaths()
    {
        var root = FindRepoRoot() ?? throw new FileNotFoundException($"{manifest_file} not found in any parent directory");
        var manifest = Path.Combine(root, manifest_file);

        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft Visual Studio\Installer\vswhere.exe");

        if (!File.Exists(vswhere))
            throw new FileNotFoundException($"vswhere.exe not found: {vswhere}");

        var vcpkg_root = Environment.GetEnvironmentVariable("VCPKG_ROOT");

        if (string.IsNullOrWhiteSpace(vcpkg_root))
            throw new Exception("VCPKG_ROOT is not set");

        if (!Directory.Exists(vcpkg_root))
            throw new Exception($"VCPKG_ROOT not found: {vcpkg_root}");

        var vcpkg = Path.Combine(vcpkg_root, "vcpkg.exe");

        if (!File.Exists(vcpkg))
            throw new FileNotFoundException($"vcpkg.exe not found in VCPKG_ROOT: {vcpkg}");

        var msbuild = LocateMSBuild(vswhere);

        var src = Path.Combine(root, "src");
        var build = Path.Combine(root, "build");
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
            Console.WriteLine(current_directory);
            if (File.Exists(Path.Combine(current_directory, manifest_file)))
                return current_directory;
        }

        return null;
    }


    private static string LocateMSBuild(string vswherePath)
    {
        var psi = new ProcessStartInfo(vswherePath,
            "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\amd64\\MSBuild.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("vswhere.exe failed to start");

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        string? found = output
            .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(found))
            throw new FileNotFoundException("MSBuild.exe not found");

        return found;
    }
}
