using System.Diagnostics;

public static class Paths
{
    public static string vswhere { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Microsoft Visual Studio\\Installer\\vswhere.exe");
    public static string MSBuild { get; set; } = string.Empty;
    public static string vcpkg { get; set; } = string.Empty;
    public static string root { get; set; } = string.Empty;
    public static string src => Path.Combine(root, "src");
    public static string build => Path.Combine(root, "build");

    static Paths()
    {
        var current_dir = Environment.CurrentDirectory;

        while (!string.IsNullOrEmpty(current_dir))
        {
            string manifest = Path.Combine(current_dir, "cv.json");

            if (File.Exists(manifest))
            {
                root = current_dir;
                break;
            }

            var parent = Directory.GetParent(current_dir);

            if (parent == null)
                break;

            current_dir = parent.FullName;
        }

        if (string.IsNullOrWhiteSpace(root))
            throw new FileNotFoundException("cv.json not found in any parent directory");

        if (!File.Exists(vswhere))
            throw new FileNotFoundException($"vswhere.exe not found: {vswhere}");

        var vcpkg_root = Environment.GetEnvironmentVariable("VCPKG_ROOT");

        if (string.IsNullOrWhiteSpace(vcpkg_root))
            throw new Exception("VCPKG_ROOT is not set");

        vcpkg = vcpkg_root;

        var process = Process.Start(new ProcessStartInfo(Paths.vswhere, "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\amd64\\MSBuild.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

        if (process == null)
            throw new InvalidOperationException("vswhere.exe failed to start");

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var path = output?
            .Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(path))
            throw new FileNotFoundException($"MSBuild.exe not found: {Paths.MSBuild}");

        MSBuild = path;
    }
}
