using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management.Automation;
using System.Text.Json;
using Microsoft.Build.Construction;
using Microsoft.VisualStudio.Setup.Configuration;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace cxx;

public static class VisualStudio
{
    public enum BuildConfiguration
    {
        Debug,
        Release
    }

    public static Task<Dictionary<string, string>> DevEnv => _lazyEnv.Value;
    private static readonly Lazy<Task<Dictionary<string, string>>> _lazyEnv =
        new(async () =>
        {
            var devPrompt = App.Find.DeveloperPrompt(App.Paths.Tools.VSWhere);

            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c call \"{devPrompt}\" -arch=amd64 -host_arch=amd64 && set",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(startInfo)
                ?? throw new Exception("Failed to start dev environment capture process");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrWhiteSpace(stderr))
                Console.Error.WriteLine(stderr);

            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                var key = line[..eq];
                var value = line[(eq + 1)..];

                env[key] = value;
            }

            return env;
        });

    public static class DevEnvironmentTools
    {
        private static readonly string CacheFile = Path.Combine(App.Paths.AppLocal, "DevToolsCache.json");
        private static readonly string[] ToolNames = { "MSBuild.exe", "lib.exe", "link.exe", "rc.exe" };

        // Lazy cache: either load synchronously from JSON or compute async if needed
        private static readonly Lazy<Task<ConcurrentDictionary<string, string>>> _tools = new(() =>
        {
            // If JSON exists, load synchronously for instant access
            if (File.Exists(CacheFile))
            {
                try
                {
                    var json = File.ReadAllText(CacheFile);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                        return Task.FromResult(new ConcurrentDictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase));
                }
                catch
                {
                    // Ignore errors and fall back to async computation
                }
            }

            // Otherwise compute asynchronously
            return ComputeToolsAsync();
        });

        private static async Task<ConcurrentDictionary<string, string>> ComputeToolsAsync()
        {
            var dict = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await Parallel.ForEachAsync(ToolNames, async (tool, _) =>
            {
                var path = await GetCommandFromDevEnv(tool);
                dict[tool] = path;
            });

            // Save JSON for next runs
            try
            {
                var jsonSave = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(CacheFile, jsonSave);
            }
            catch
            {
                // ignore save errors
            }

            return dict;
        }

        private static Task<ConcurrentDictionary<string, string>> Tools => _tools.Value;

        private static async Task<string> GetTool(string name) => (await Tools)[name];

        // Public accessors using ValueTask for minimal overhead
        public static ValueTask<string> MSBuild() => new(GetTool("MSBuild.exe"));
        public static ValueTask<string> Lib() => new(GetTool("lib.exe"));
        public static ValueTask<string> Link() => new(GetTool("link.exe"));
        public static ValueTask<string> RC() => new(GetTool("rc.exe"));
    }

    public static async Task<string> GetCommandFromDevEnv(string command)
    {
        var devEnv = await DevEnv;

        if (!devEnv.TryGetValue("PATH", out var pathValue) &&
            !devEnv.TryGetValue("Path", out pathValue))
        {
            throw new KeyNotFoundException("PATH environment variable not found in developer environment.");
        }

        string script = $@"
            $env:PATH = '{pathValue}'
            where.exe {command}
        ";

        using var ps = PowerShell.Create();
        ps.AddScript(script);

        foreach (var kvp in devEnv)
            ps.Runspace.SessionStateProxy.SetVariable(kvp.Key, kvp.Value);

        var results = await Task.Run(() => ps.Invoke());

        var lines = new List<string>();

        foreach (var r in results)
        {
            var line = r?.ToString();
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

        if (lines.Count == 0)
            throw new FileNotFoundException($"{command} not found in the developer environment PATH.");

        return lines[0];
    }

    private static readonly SetupConfiguration setupConfiguration = new SetupConfiguration();
    private static readonly Lazy<ISetupInstance?> _latest = new(GetLatestInstance);

    /// <summary>The latest installed Visual Studio instance (any edition, including Build Tools).</summary>
    public static ISetupInstance? Latest => _latest.Value;

    /// <summary>The installation path of the latest Visual Studio instance, or null if none installed.</summary>
    public static string? InstallPath => Latest?.GetInstallationPath();

    /// <summary>The vswhere path of the latest Visual Studio instance (64-bit).</summary>
    public static string VSWherePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Microsoft Visual Studio", "Installer", "vswhere.exe");

    /// <summary>The MSBuild path of the latest Visual Studio instance (64-bit).</summary>
    public static string? MSBuildPath => InstallPath is string path
        ? Path.Combine(path, "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe")
        : null;

    /// <summary>The ninja path of the latest Visual Studio instance (64-bit).</summary>
    public static string? NinjaPath => InstallPath is string path
        ? Path.Combine(path, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "Ninja", "ninja.exe")
        : null;

    /// <summary>The vcpkg path of the latest Visual Studio instance (64-bit).</summary>
    public static string? VcpkgPath => InstallPath is string path
        ? Path.Combine(path, "VC", "vcpkg", "vcpkg.exe")
        : null;

    /// <summary>The path to cl.exe compiler of the latest Visual Studio instance (x64).</summary>
    public static string? ClPath => LatestMSVCVersionPath() is string path
        ? Path.Combine(path, "bin", "Hostx64", "x64", "cl.exe")
        : null;

    /// <summary>Finds the latest MSVC version folder inside VC\Tools\MSVC.</summary>
    private static string? LatestMSVCVersionPath()
    {
        if (InstallPath is null) return null;

        var vcRoot = Path.Combine(InstallPath, "VC", "Tools", "MSVC");
        if (!Directory.Exists(vcRoot)) return null;

        return Directory.GetDirectories(vcRoot)
            .OrderByDescending(Path.GetFileName)
            .FirstOrDefault();
    }

    /// <summary>Finds the latest installed Visual Studio instance.</summary>
    private static ISetupInstance? GetLatestInstance()
    {
        var enumInstances = setupConfiguration.EnumAllInstances();

        ISetupInstance? latest = null;
        ISetupInstance[] buffer = new ISetupInstance[1];
        int fetched;

        do
        {
            enumInstances.Next(1, buffer, out fetched);
            if (fetched > 0)
            {
                var instance = buffer[0];
                if (latest == null || string.Compare(instance.GetInstallationVersion(),
                    latest.GetInstallationVersion(), StringComparison.Ordinal) > 0)
                {
                    latest = instance;
                }
            }
        } while (fetched > 0);

        return latest;
    }

    public static async Task<int> Build(BuildConfiguration config)
    {
        Directory.CreateDirectory(App.Paths.Core.Build);

        if (await Generate() != 0)
            return 1;

        if (string.IsNullOrWhiteSpace(App.Paths.Tools.MSBuild))
            throw new InvalidOperationException("MSBuild path not set.");

        var args = $"-nologo -v:minimal /p:Configuration={(config == BuildConfiguration.Debug ? "Debug" : "Release")} /p:Platform=x64";
        var process = Process.Start(new ProcessStartInfo(App.Paths.Tools.MSBuild, args) { WorkingDirectory = App.Paths.Core.Build }) ?? throw new InvalidOperationException("Failed to start MSBuild");
        process.WaitForExit();

        Console.Error.WriteLine();

        return 0;
    }

    public static int Clean()
    {
        if (!Directory.Exists(App.Paths.Core.Build))
            return 1;

        string[] dirs = { "debug", "release" };

        foreach (string dir in dirs)
        {
            var markedDir = Path.Combine(App.Paths.Core.Build, dir);

            if (Directory.Exists(markedDir))
                Directory.Delete(markedDir, true);
        }

        string[] files = { App.Paths.Core.SolutionFile, App.Paths.Core.ProjectFile };

        foreach (string file in files)
        {
            var markedFile = Path.Combine(App.Paths.Core.Build, file);

            if (File.Exists(markedFile))
                File.Delete(markedFile);
        }

        return 0;
    }

    public static async Task<string> GetWindowsSdkExecutablePath()
    {
        var devEnv = await DevEnv;

        if (!devEnv.TryGetValue("WindowsSdkVerBinPath", out var sdkPath))
            throw new KeyNotFoundException("WindowsSdkVerBinPath not found in developer environment.");

        if (!Directory.Exists(sdkPath))
            throw new DirectoryNotFoundException($"Windows SDK path does not exist: {sdkPath}");

        return Path.Combine(sdkPath, "x64");
    }

    private static async Task SaveEnvToJson(Dictionary<string, string> env)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        using var stream = File.Create(Path.Combine(App.Paths.AppLocal, "DevEnv.json"));
        await JsonSerializer.SerializeAsync(stream, env, options);
    }

    public static void ApplyEnvToProcess(ProcessStartInfo startInfo, Dictionary<string, string> env)
    {
        if (startInfo.Environment == null)
            throw new InvalidOperationException("ProcessStartInfo.Environment is not available. Make sure UseShellExecute = false.");

        foreach (var kvp in env)
        {
            startInfo.Environment[kvp.Key] = kvp.Value;
        }
    }

    private static async Task<int> GenerateSolution()
    {
        var solutionModel = new SolutionModel();

        solutionModel.AddPlatform("x64");
        solutionModel.AddPlatform("x86");

        var solutionProject = solutionModel.AddProject("app.vcxproj");

        solutionProject.Id = Guid.NewGuid();

        await SolutionSerializers.SlnXml.SaveAsync(App.Paths.Core.SolutionFile, solutionModel, new CancellationToken());

        return 0;
    }

    public static async Task<int> Generate()
    {
        await GenerateSolution();

        var project = ProjectRootElement.Create();
        project.DefaultTargets = "Build";
        project.ToolsVersion = null;

        string[] configurations = { "Debug", "Release" };
        string[] platforms = { "Win32", "x64" };

        // ----- 1. Globals -----
        var globals = project.AddPropertyGroup();
        globals.Label = "Globals";
        globals.AddProperty("VCProjectVersion", "18.0");
        globals.AddProperty("Keyword", "Win32Proj");
        globals.AddProperty("ProjectGuid", "{4985344b-071c-4114-a0bb-41d2b55773cd}");
        globals.AddProperty("RootNamespace", "app");
        globals.AddProperty("WindowsTargetPlatformVersion", "10.0");
        globals.AddProperty("UseMultiToolTask", "true");
        globals.AddProperty("EnforceProcessCountAcrossBuilds", "true");

        // ----- 2. Import Default.props -----
        project.AddImport("$(VCTargetsPath)\\Microsoft.Cpp.Default.props");

        // ----- 3. Configuration PropertyGroups -----
        foreach (var config in configurations)
        {
            foreach (var platform in platforms)
            {
                var group = project.AddPropertyGroup();
                group.Condition = $"'$(Configuration)|$(Platform)'=='{config}|{platform}'";
                group.Label = "Configuration";

                group.AddProperty("ConfigurationType", "Application");
                group.AddProperty("UseDebugLibraries", config == "Debug" ? "true" : "false");
                group.AddProperty("PlatformToolset", "v145");
                group.AddProperty("CharacterSet", "Unicode");
                group.AddProperty("EnableUnitySupport", "false");
                group.AddProperty("IntDir", $@"$(SolutionDir)\{config.ToLowerInvariant()}\obj\");
                group.AddProperty("OutDir", $@"$(SolutionDir)\{config.ToLowerInvariant()}\");
            }
        }

        // ----- 4. ProjectConfigurations (must be AFTER config groups) -----
        var projectConfigurations = project.AddItemGroup();
        projectConfigurations.Label = "ProjectConfigurations";

        foreach (var config in configurations)
        {
            foreach (var platform in platforms)
            {
                var item = projectConfigurations.AddItem("ProjectConfiguration", $"{config}|{platform}");
                item.AddMetadata("Configuration", config);
                item.AddMetadata("Platform", platform);
            }
        }

        // ----- 5. Import Microsoft.Cpp.props -----
        project.AddImport(@"$(VCTargetsPath)\Microsoft.Cpp.props");

        // ----- 6. ExtensionSettings ImportGroup -----
        var extensionSettings = project.AddImportGroup();
        extensionSettings.Label = "ExtensionSettings";

        // ----- 7. Shared ImportGroup -----
        var shared = project.AddImportGroup();
        shared.Label = "Shared";

        // ----- 8. Per-configuration PropertySheets -----
        foreach (var config in configurations)
        {
            foreach (var platform in platforms)
            {
                var propertySheets = project.AddImportGroup();
                propertySheets.Label = "PropertySheets";
                propertySheets.Condition = $"'$(Configuration)|$(Platform)'=='{config}|{platform}'";

                var import = propertySheets.AddImport(@"$(UserRootDir)\Microsoft.Cpp.$(Platform).user.props");
                import.Condition = $"exists('$(UserRootDir)\\Microsoft.Cpp.$(Platform).user.props')";
                import.Label = "LocalAppDataPlatform";
            }
        }

        // ----- 9. UserMacros -----
        var user_macros = project.AddPropertyGroup();
        user_macros.Label = "UserMacros";

        // ----- 10. ItemDefinitionGroups -----
        foreach (var config in configurations)
        {
            foreach (var platform in platforms)
            {
                var projectSettings = project.AddItemDefinitionGroup();
                projectSettings.Condition = $"'$(Configuration)|$(Platform)'=='{config}|{platform}'";

                // ----- ClCompile -----
                var cl_compile = projectSettings.AddItemDefinition("ClCompile");

                cl_compile.AddMetadata("WarningLevel", "Level4", false);
                cl_compile.AddMetadata("TreatWarningAsError", "true", false);
                cl_compile.AddMetadata("SDLCheck", "true", false);
                cl_compile.AddMetadata("ConformanceMode", "true", false);
                cl_compile.AddMetadata("LanguageStandard", "stdcpplatest", false);
                cl_compile.AddMetadata("LanguageStandard_C", "stdclatest", false);
                cl_compile.AddMetadata("BuildStlModules", "true", false);

                // PreprocessorDefinitions
                string preprocessor = config switch
                {
                    "Debug" when platform == "Win32" => "WIN32;_DEBUG;_CONSOLE;%(PreprocessorDefinitions)",
                    "Release" when platform == "Win32" => "WIN32;NDEBUG;_CONSOLE;%(PreprocessorDefinitions)",
                    "Debug" when platform == "x64" => "_DEBUG;_CONSOLE;%(PreprocessorDefinitions)",
                    "Release" when platform == "x64" => "NDEBUG;_CONSOLE;%(PreprocessorDefinitions)",
                    _ => "%(PreprocessorDefinitions)"
                };
                cl_compile.AddMetadata("PreprocessorDefinitions", preprocessor, false);

                // Release-specific flags
                if (config == "Release")
                {
                    cl_compile.AddMetadata("FunctionLevelLinking", "true", false);
                    cl_compile.AddMetadata("IntrinsicFunctions", "true", false);
                }

                // ----- Link -----
                var link = projectSettings.AddItemDefinition("Link");
                link.AddMetadata("SubSystem", "Console", false);
                link.AddMetadata("GenerateDebugInformation", "true", false);
            }
        }

        // ----- 11. Empty ItemGroup -----
        project.AddItemGroup();

        // ----- 12. Import Microsoft.Cpp.targets -----
        project.AddImport(@"$(VCTargetsPath)\Microsoft.Cpp.targets");

        // ----- 13. ExtensionTargets ImportGroup -----
        var extensionTargets = project.AddImportGroup();
        extensionTargets.Label = "ExtensionTargets";

        // ----- 14. Vcpkg PropertyGroup -----
        var vcpkg = project.AddPropertyGroup();
        vcpkg.Label = "Vcpkg";
        vcpkg.AddProperty("VcpkgEnableManifest", "true");
        vcpkg.AddProperty("VcpkgUseStatic", "true");
        vcpkg.AddProperty("VcpkgUseMD", "true");

        // ----- 15. Add sources from "src" folder -----
        var sourceFiles = Directory.GetFiles(App.Paths.Core.Src, "*.cpp");
        var moduleFiles = Directory.GetFiles(App.Paths.Core.Src, "*.ixx");
        var headerFiles = Directory.GetFiles(App.Paths.Core.Src, "*.h");

        var sources = project.AddItemGroup();

        foreach (var sourceFile in sourceFiles)
            sources.AddItem("ClCompile", Path.GetRelativePath(App.Paths.Core.Build, sourceFile).Replace('\\', '/'));

        foreach (var moduleFile in moduleFiles)
            sources.AddItem("ClCompile", Path.GetRelativePath(App.Paths.Core.Build, moduleFile).Replace('\\', '/'));

        foreach (var headerFile in headerFiles)
            sources.AddItem("ClInclude", Path.GetRelativePath(App.Paths.Core.Build, headerFile).Replace('\\', '/'));

        project.Save(App.Paths.Core.ProjectFile);

        return 0;
    }
}
