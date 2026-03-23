using System.Diagnostics;
using System.Text.Json;

namespace CXX;

public static class Project
{
    public static readonly string AppLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cxx");
    public static readonly string AppRoaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cxx");
    public static class ProjectTypes
    {
        public const string Exe = "exe";
        public const string Lib = "lib";
    }

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
        public string type { get; set; } = ProjectTypes.Exe;
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
            var newConfig = new Config();
            Save(newConfig, path);
            return newConfig;
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<Config>(json, Options)
               ?? new Config();
        config.type = NormalizeType(config.type);
        return config;
    }

    public static void Save(Config config, string path)
    {
        config.type = NormalizeType(config.type);
        var json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(path, json);
    }

    public static string NormalizeType(string? projectType) =>
        string.Equals(projectType, ProjectTypes.Lib, StringComparison.OrdinalIgnoreCase)
            ? ProjectTypes.Lib
            : ProjectTypes.Exe;

    public static bool IsLibrary(string? projectType) =>
        string.Equals(NormalizeType(projectType), ProjectTypes.Lib, StringComparison.Ordinal);

    public static bool IsLibrary(Config config) => IsLibrary(config.type);

    public static string GetConfigurationType(Config config) =>
        IsLibrary(config) ? "StaticLibrary" : "Application";

    public static string GetOutputBaseName() =>
        Path.GetFileNameWithoutExtension(Paths.ProjectFile);

    public static string GetPublicIncludeStem(Config config) => config.name;

    public static string GetPublicIncludeDirectory(Config config) =>
        Path.Combine(Paths.Include, GetPublicIncludeStem(config));

    public static string GetPublicHeaderFile(Config config) =>
        Path.Combine(GetPublicIncludeDirectory(config), $"{GetPublicIncludeStem(config)}.hpp");

    public static string GetPublicHeaderInclude(Config config) =>
        $"{GetPublicIncludeStem(config)}/{GetPublicIncludeStem(config)}.hpp";

    public static string GetOutputDirectory(BuildConfiguration config) =>
        config == BuildConfiguration.Debug ? Paths.Debug : Paths.Release;

    public static string GetBinaryFile(BuildConfiguration config) =>
        Path.Combine(GetOutputDirectory(config), $"{GetOutputBaseName()}.{(IsLibrary(Current) ? "lib" : "exe")}");

    public static string GetCpsFile(BuildConfiguration config) =>
        Path.Combine(GetOutputDirectory(config), $"{GetOutputBaseName()}.cps");

    public static async Task<int> New(string projectType = ProjectTypes.Exe)
    {
        var manifestFile = Path.Combine(Environment.CurrentDirectory, Manifest.Filename);
        var vcpkgManifestFile = Path.Combine(Environment.CurrentDirectory, "vcpkg.json");
        var vcpkgConfigurationFile = Path.Combine(Environment.CurrentDirectory, "vcpkg-configuration.json");
        var srcDirectory = Path.Combine(Environment.CurrentDirectory, "src");
        var includeDirectory = Path.Combine(Environment.CurrentDirectory, "include");

        var manifestExists = File.Exists(manifestFile);
        var vcpkgExists = File.Exists(vcpkgManifestFile) || File.Exists(vcpkgConfigurationFile);
        var created = new List<string>();
        var skipped = new List<string>();
        var requestedProjectType = NormalizeType(projectType);
        var effectiveProjectType = requestedProjectType;

        var config = manifestExists
            ? Load(manifestFile)
            : new Config
            {
                name = $"{App.MetaData.Name}-project",
                version = "0.0.0",
                type = requestedProjectType
            };

        effectiveProjectType = NormalizeType(config.type);

        if (!manifestExists)
        {
            Save(config, manifestFile);
            created.Add(Manifest.Filename);
        }
        else
        {
            skipped.Add(Manifest.Filename);

            if (!string.Equals(requestedProjectType, effectiveProjectType, StringComparison.Ordinal))
                Print.Err($"Using existing project type '{effectiveProjectType}' from {Manifest.Filename}.", ConsoleColor.DarkYellow);
        }

        if (!vcpkgExists)
        {
            var vcpkgProcessInfo = Exe.Vcpkg;
            vcpkgProcessInfo.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
            vcpkgProcessInfo.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";
            var vcpkgArgs = IsLibrary(effectiveProjectType)
                ? new[] { "new", "--name", config.name, "--version", config.version }
                : new[] { "new", "--application" };
            var vcpkgExitCode = await App.Run(vcpkgProcessInfo, vcpkgArgs);

            if (vcpkgExitCode != 0)
                return vcpkgExitCode;

            created.Add("vcpkg.json/vcpkg-configuration.json");
        }
        else
        {
            skipped.Add("vcpkg.json/vcpkg-configuration.json");
        }

        if (IsLibrary(effectiveProjectType))
        {
            var publicHeaderFile = Path.Combine(includeDirectory, GetPublicHeaderInclude(config).Replace('/', Path.DirectorySeparatorChar));
            var publicHeaderPath = Path.GetRelativePath(Environment.CurrentDirectory, publicHeaderFile).Replace('\\', '/');

            if (!File.Exists(publicHeaderFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(publicHeaderFile)!);
                await File.WriteAllTextAsync(
                    publicHeaderFile,
                    @"
#pragma once

auto library_entry_point() -> int;
".Trim()
                );

                created.Add(publicHeaderPath);
            }
            else
            {
                skipped.Add(publicHeaderPath);
            }
        }

        var sourceFile = IsLibrary(effectiveProjectType) ? "lib.cpp" : "app.cpp";
        var sourcePath = Path.Combine(srcDirectory, sourceFile);

        if (!File.Exists(sourcePath))
        {
            var srcPath = Directory.CreateDirectory(srcDirectory).FullName;
            var appFile = Path.Combine(srcPath, sourceFile);

            if (IsLibrary(effectiveProjectType))
            {
                await File.WriteAllTextAsync(
                    appFile,
                    $@"
#include <{GetPublicHeaderInclude(config)}>

auto library_entry_point() -> int {{
    return 0;
}}
".Trim()
                );
            }
            else
            {
                await File.WriteAllTextAsync(
                    appFile,
                    @"
#include <print>

auto wmain() -> int
{
    std::println(""Hello, World!"");

    return 0;
}
".Trim()
                );
            }

            created.Add($"src/{sourceFile}");
        }
        else
        {
            skipped.Add($"src/{sourceFile}");
        }

        Print.Err($"Initialized {App.MetaData.Name} {effectiveProjectType} project", ConsoleColor.Green);

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

    public static Config Current => Load(Paths.Manifest);

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
            Include: Path.Combine(root, "include"),
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
        string Include,
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
