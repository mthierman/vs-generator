using System.Diagnostics;
using System.Text.Json;

namespace CXX;

public static class Project
{
    public enum BuildConfiguration
    {
        Debug,
        Release
    }

    public static class Exe
    {
        public static ProcessStartInfo Debug => new() { FileName = Path.Combine(App.Paths.Project.Build, "debug", "app.exe") };
        public static ProcessStartInfo Release => new() { FileName = Path.Combine(App.Paths.Project.Build, "release", "app.exe") };
        public static ProcessStartInfo CXX => new() { FileName = Environment.ProcessPath };
        public static ProcessStartInfo VSWhere => new() { FileName = VisualStudio.VSWherePath };
        public static ProcessStartInfo MSBuild => new() { FileName = VisualStudio.MSBuildPath };
        public static ProcessStartInfo CL => new() { FileName = VisualStudio.ClPath };
        public static ProcessStartInfo RC => new() { FileName = VisualStudio.RcPath };
        public static ProcessStartInfo Vcpkg => new() { FileName = VisualStudio.VcpkgPath };
        public static ProcessStartInfo Ninja => new() { FileName = VisualStudio.NinjaPath };
        public static ProcessStartInfo ClangFormat => new() { FileName = VisualStudio.ClangFormatPath };
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
        var manifestFile = Path.Combine(Environment.CurrentDirectory, App.Paths.ManifestFileName);

        if (Directory.EnumerateFileSystemEntries(Environment.CurrentDirectory).Any() ||
            File.Exists(manifestFile))
        {
            App.Print.Err($"Directory was not empty.", ConsoleColor.Red);

            return 1;
        }

        var config = new Config
        {
            name = $"{App.MetaData.Name}-project",
            version = "0.0.0"
        };

        Save(config, manifestFile);

        App.Print.Err($"Generated new {App.MetaData.Name} project", ConsoleColor.Green);
        Console.Error.WriteLine();
        App.Print.Err(JsonSerializer.Serialize(config,
            new JsonSerializerOptions { WriteIndented = true }),
            ConsoleColor.DarkGreen);

        var vcpkgProcessInfo = Exe.Vcpkg;
        vcpkgProcessInfo.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
        vcpkgProcessInfo.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";
        await App.Run(vcpkgProcessInfo, "new", "--application");

        await File.WriteAllTextAsync(
        Path.Combine(Directory.CreateDirectory(App.Paths.Project.Src).FullName, "app.cpp"),
        @"
#include <print>

auto wmain() -> int {
    std::println(""Hello, World!"");
    return 0;
}
".Trim()
        );

        return 0;
    }
}
