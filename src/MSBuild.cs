using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Management.Automation;
using System.Text.Json;
using Microsoft.Build.Construction;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace cxx;

public static class MSBuild
{
    public enum BuildConfiguration
    {
        Debug,
        Release
    }

    public static class DevEnvironmentTools
    {
        private static readonly string CacheFile = Path.Combine(Project.Paths.AppLocal, "DevToolsCache.json");
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

    public static class DevEnvironmentProvider
    {
        private static readonly Lazy<Task<Dictionary<string, string>>> _lazyEnv =
            new(LoadOrCreateAsync);
        public static Task<Dictionary<string, string>> Environment => _lazyEnv.Value;

        private static async Task<Dictionary<string, string>> LoadOrCreateAsync()
        {
            // if (File.Exists(Project.SystemFolders.DevEnvJson))
            // {
            //     try
            //     {
            //         var json = await File.ReadAllTextAsync(Project.SystemFolders.DevEnvJson);
            //         var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            //         if (dict is not null)
            //             return dict;
            //     }
            //     catch
            //     {
            //     }
            // }

            var fresh = await GetDevEnv();

            // try
            // {
            //     var json = JsonSerializer.Serialize(fresh, new JsonSerializerOptions
            //     {
            //         WriteIndented = true
            //     });

            //     await File.WriteAllTextAsync(Project.SystemFolders.DevEnvJson, json);
            // }
            // catch
            // {
            // }

            return fresh;
        }
    }

    public static async Task<Dictionary<string, string>> GetDevEnv()
    {
        var devPrompt = Find.DeveloperPrompt(Project.Tools.VSWhere);

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.Arguments = $"/c call \"{devPrompt}\" -arch=amd64 -host_arch=amd64 && set";

        using var process = Process.Start(startInfo)!;

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (!string.IsNullOrEmpty(stderr))
            Console.Error.WriteLine(stderr);

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            int eq = trimmed.IndexOf('=');

            if (eq <= 0)
                continue;

            string key = trimmed[..eq];
            string value = trimmed[(eq + 1)..];

            env[key] = value;
        }

        return env;
    }

    public static async Task<string> GetCommandFromDevEnv(string command)
    {
        var devEnv = await DevEnvironmentProvider.Environment;

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

    // public static async Task SaveEnvToJson(Dictionary<string, string> env)
    // {
    //     var options = new JsonSerializerOptions
    //     {
    //         WriteIndented = true,
    //         Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    //     };

    //     using var stream = File.Create(Project.SystemFolders.DevEnvJson);
    //     await JsonSerializer.SerializeAsync(stream, env, options);
    // }

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

        await SolutionSerializers.SlnXml.SaveAsync(Project.Core.SolutionFile, solutionModel, new CancellationToken());

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
        var sourceFiles = Directory.GetFiles(Project.Core.Src, "*.cpp");
        var moduleFiles = Directory.GetFiles(Project.Core.Src, "*.ixx");
        var headerFiles = Directory.GetFiles(Project.Core.Src, "*.h");

        var sources = project.AddItemGroup();

        foreach (var sourceFile in sourceFiles)
            sources.AddItem("ClCompile", Path.GetRelativePath(Project.Core.Build, sourceFile).Replace('\\', '/'));

        foreach (var moduleFile in moduleFiles)
            sources.AddItem("ClCompile", Path.GetRelativePath(Project.Core.Build, moduleFile).Replace('\\', '/'));

        foreach (var headerFile in headerFiles)
            sources.AddItem("ClInclude", Path.GetRelativePath(Project.Core.Build, headerFile).Replace('\\', '/'));

        project.Save(Project.Core.ProjectFile);

        return 0;
    }

    public static async Task<int> Build(BuildConfiguration config)
    {
        Directory.CreateDirectory(Project.Core.Build);

        if (await Generate() != 0)
            return 1;

        if (string.IsNullOrWhiteSpace(Project.Tools.MSBuild))
            throw new InvalidOperationException("MSBuild path not set.");

        var args = $"-nologo -v:minimal /p:Configuration={(config == BuildConfiguration.Debug ? "Debug" : "Release")} /p:Platform=x64";
        var process = Process.Start(new ProcessStartInfo(Project.Tools.MSBuild, args) { WorkingDirectory = Project.Core.Build }) ?? throw new InvalidOperationException("Failed to start MSBuild");
        process.WaitForExit();

        Console.Error.WriteLine();

        return 0;
    }

    public static int Clean()
    {
        if (!Directory.Exists(Project.Core.Build))
            return 1;

        string[] dirs = { "debug", "release" };

        foreach (string dir in dirs)
        {
            var markedDir = Path.Combine(Project.Core.Build, dir);

            if (Directory.Exists(markedDir))
                Directory.Delete(markedDir, true);
        }

        string[] files = { Project.Core.SolutionFile, Project.Core.ProjectFile };

        foreach (string file in files)
        {
            var markedFile = Path.Combine(Project.Core.Build, file);

            if (File.Exists(markedFile))
                File.Delete(markedFile);
        }

        return 0;
    }
}
