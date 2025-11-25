using System.Diagnostics;

public partial class App
{
    private static readonly Lazy<EnvironmentPaths> _environmentPaths = new Lazy<EnvironmentPaths>(InitializeEnvironmentPaths);
    public static EnvironmentPaths Paths = _environmentPaths.Value;

    public sealed record EnvironmentPaths(
        string root,
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
        var manifest_file = string.Empty;
        var root = string.Empty;

        var current_directory = Environment.CurrentDirectory;

        while (!string.IsNullOrEmpty(current_directory))
        {
            var manifest = Path.Combine(current_directory, "cv.jsonc");

            if (File.Exists(manifest))
            {
                manifest_file = manifest;
                root = current_directory;

                break;
            }

            var parent = Directory.GetParent(current_directory);

            if (parent == null)
                break;

            current_directory = parent.FullName;
        }

        if (root is null)
            throw new FileNotFoundException("cv.jsonc not found in any parent directory");

        var vswhere_path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft Visual Studio\Installer\vswhere.exe");

        if (!File.Exists(vswhere_path))
            throw new FileNotFoundException($"vswhere.exe not found: {vswhere_path}");

        var vcpkg_root = Environment.GetEnvironmentVariable("VCPKG_ROOT");

        if (string.IsNullOrWhiteSpace(vcpkg_root))
            throw new Exception("VCPKG_ROOT is not set");

        var vcpkg_path = Path.Combine(vcpkg_root, "vcpkg.exe");

        if (!File.Exists(vcpkg_path))
            throw new FileNotFoundException($"vcpkg.exe not found in VCPKG_ROOT: {vcpkg_path}");

        var msbuild_path = LocateMSBuild(vswhere_path);

        var src = Path.Combine(root, "src");
        var build = Path.Combine(root, "build");
        var solution_file = Path.Combine(root, "build", "app.slnx");
        var project_file = Path.Combine(root, "build", "app.vcxproj");

        return new EnvironmentPaths(root, vswhere_path, msbuild_path, vcpkg_path, src, build, solution_file, project_file);
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
