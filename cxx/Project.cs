using System.Diagnostics;
using System.Text.Json;

namespace CXX;

public static class Project
{
    public static readonly string AppLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cxx");
    public static readonly string AppRoaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cxx");

    public static class Manifest
    {
        public static readonly string Filename = "cxx.jsonc";
    }

    public enum BuildConfiguration
    {
        Debug,
        Release
    }

    public sealed class Config
    {
        public string name { get; set; } = $"blank-project";
        public string version { get; set; } = "0.0.0";
    }

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static Config Load(string path)
    {
        if (!File.Exists(path))
        {
            var config = new Config();
            Save(config, path);
            return config;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Config>(json, Options)
               ?? new Config();
    }

    public static void Save(Config config, string path)
    {
        var json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(path, json);
    }

    public static async Task<int> New()
    {
        var manifestFile = Path.Combine(Environment.CurrentDirectory, Manifest.Filename);
        var vcpkgManifestFile = Path.Combine(Environment.CurrentDirectory, "vcpkg.json");
        var vcpkgConfigurationFile = Path.Combine(Environment.CurrentDirectory, "vcpkg-configuration.json");
        var srcDirectory = Path.Combine(Environment.CurrentDirectory, "src");

        var manifestExists = File.Exists(manifestFile);
        var vcpkgExists = File.Exists(vcpkgManifestFile) || File.Exists(vcpkgConfigurationFile);
        var srcExists = Directory.Exists(srcDirectory);
        var created = new List<string>();
        var skipped = new List<string>();

        var config = new Config
        {
            name = $"{App.MetaData.Name}-project",
            version = "0.0.0"
        };

        if (!manifestExists)
        {
            Save(config, manifestFile);
            created.Add(Manifest.Filename);
        }
        else
        {
            skipped.Add(Manifest.Filename);
        }

        if (!vcpkgExists)
        {
            var vcpkgProcessInfo = Exe.Vcpkg;
            vcpkgProcessInfo.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
            vcpkgProcessInfo.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";
            var vcpkgExitCode = await App.Run(vcpkgProcessInfo, "new", "--application");

            if (vcpkgExitCode != 0)
                return vcpkgExitCode;

            created.Add("vcpkg.json/vcpkg-configuration.json");
        }
        else
        {
            skipped.Add("vcpkg.json/vcpkg-configuration.json");
        }

        if (!srcExists)
        {
            var appFile = Path.Combine(Directory.CreateDirectory(srcDirectory).FullName, "app.cpp");

            await File.WriteAllTextAsync(
                appFile,
                @"
#include <print>

auto wmain() -> int {
    std::println(""Hello, World!"");
    return 0;
}
".Trim()
            );

            created.Add("src/app.cpp");
        }
        else
        {
            skipped.Add("src");
        }

        Print.Err($"Initialized {App.MetaData.Name} project", ConsoleColor.Green);

        if (!manifestExists)
        {
            Console.Error.WriteLine();
            Print.Err(JsonSerializer.Serialize(config,
                new JsonSerializerOptions { WriteIndented = true }),
                ConsoleColor.DarkGreen);
        }

        if (created.Count > 0)
            Print.Err($"Created: {string.Join(", ", created)}", ConsoleColor.DarkGreen);

        if (skipped.Count > 0)
            Print.Err($"Skipped existing: {string.Join(", ", skipped)}", ConsoleColor.DarkYellow);

        return 0;
    }

    public static ProjectPaths Paths => _paths.Value;
    private static readonly Lazy<ProjectPaths> _paths = new(() =>
    {
        var cwd = Environment.CurrentDirectory;
        var root = string.Empty;

        while (!string.IsNullOrEmpty(cwd))
        {
            if (File.Exists(Path.Combine(cwd, Project.Manifest.Filename)))
                root = cwd;

            cwd = Directory.GetParent(cwd)?.FullName;
        }

        if (string.IsNullOrEmpty(root))
            throw new FileNotFoundException($"Manifest not found: {Project.Manifest.Filename}");

        return new(
            Root: root,
            Manifest: Path.Combine(root, Project.Manifest.Filename),
            Src: Path.Combine(root, "src"),
            Build: Path.Combine(root, "build"),
            Debug: Path.Combine(root, "build", "debug"),
            Release: Path.Combine(root, "build", "release"),
            Publish: Path.Combine(root, "build", "publish"),
            SolutionFile: Path.Combine(root, "build", "app.slnx"),
            ProjectFile: Path.Combine(root, "build", "app.vcxproj"));
    });

    public sealed record ProjectPaths(
        string Root,
        string Manifest,
        string Src,
        string Build,
        string Debug,
        string Release,
        string Publish,
        string SolutionFile,
        string ProjectFile);


    public static class Exe
    {
        public static ProcessStartInfo Debug => new() { FileName = Path.Combine(Paths.Build, "debug", "app.exe") };
        public static ProcessStartInfo Release => new() { FileName = Path.Combine(Paths.Build, "release", "app.exe") };
        public static ProcessStartInfo CXX => new() { FileName = Environment.ProcessPath };
        public static ProcessStartInfo VSWhere => new() { FileName = VisualStudio.VSWherePath };
        public static ProcessStartInfo MSBuild => new() { FileName = VisualStudio.MSBuildPath };
        public static ProcessStartInfo CL => new() { FileName = VisualStudio.ClPath };
        public static ProcessStartInfo RC => new() { FileName = VisualStudio.RcPath };
        public static ProcessStartInfo Vcpkg => new() { FileName = VisualStudio.VcpkgPath };
        public static ProcessStartInfo Ninja => new() { FileName = VisualStudio.NinjaPath };
        public static ProcessStartInfo ClangFormat => new() { FileName = VisualStudio.ClangFormatPath };
    }

}
