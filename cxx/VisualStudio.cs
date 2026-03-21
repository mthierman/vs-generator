using System.Diagnostics;
using Microsoft.Build.Construction;
using Microsoft.VisualStudio.Setup.Configuration;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace CXX;

public static class VisualStudio
{
    public static string? FindExeOnPath(string exeName)
    {
        var fileName = exeName + (OperatingSystem.IsWindows() && !exeName.EndsWith(".exe") ? ".exe" : "");

        var paths = Environment.GetEnvironmentVariable("PATH")?
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    ?? Array.Empty<string>();

        foreach (var path in paths)
        {
            try
            {
                var fullPath = Path.Combine(path.Trim(), fileName);

                if (File.Exists(fullPath))
                    return fullPath;
            }
            catch { }
        }

        return null;
    }

    private static readonly SemaphoreSlim ConsoleLock = new SemaphoreSlim(1, 1);

    public static Task<Dictionary<string, string>> DevEnv => _lazyEnv.Value;
    private static readonly Lazy<Task<Dictionary<string, string>>> _lazyEnv =
        new(async () =>
        {
            if (!File.Exists(VSWherePath))
                throw new FileNotFoundException($"vswhere.exe not found at {VSWherePath}");

            string installPath;
            using (var process = Process.Start(new ProcessStartInfo(VSWherePath,
                        "-latest -products * -property installationPath")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("vswhere.exe failed to start"))
            {
                installPath = (await process.StandardOutput.ReadToEndAsync())
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?
                    .Trim() ?? throw new Exception("Failed to detect Visual Studio installation path");

                await process.WaitForExitAsync();
            }

            var vsDevCmd = Path.Combine(installPath, "Common7", "Tools", "VsDevCmd.bat");

            if (!File.Exists(vsDevCmd))
                throw new FileNotFoundException("VsDevCmd.bat not found", vsDevCmd);

            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c call \"{vsDevCmd}\" -arch=amd64 -host_arch=amd64 && set",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var devCmdProcess = Process.Start(startInfo)
                ?? throw new Exception("Failed to start dev environment capture process");

            var stdoutTask = devCmdProcess.StandardOutput.ReadToEndAsync();
            var stderrTask = devCmdProcess.StandardError.ReadToEndAsync();

            await devCmdProcess.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrWhiteSpace(stderr))
                Console.Error.WriteLine(stderr);

            var env = stdout.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                            .Select(line => line.Split('=', 2))
                            .Where(parts => parts.Length == 2)
                            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);

            return env;
        });

    private static readonly Lazy<ISetupInstance?> _latestInstance = new(() =>
    {
        var setupConfiguration = new SetupConfiguration();
        var enumInstances = setupConfiguration.EnumAllInstances();
        return enumInstances.EnumerateInstances()
                            .OrderByDescending(i => i.GetInstallationVersion())
                            .FirstOrDefault();
    });
    public static ISetupInstance? Latest => _latestInstance.Value;
    public static string? InstallPath => Latest?.GetInstallationPath();
    public static string VSWherePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     "Microsoft Visual Studio", "Installer", "vswhere.exe");
    public static string? MSBuildPath => CombinePath(InstallPath, "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe");
    public static string? ClPath => CombinePath(LatestMSVCVersionPath(), "bin", "Hostx64", "x64", "cl.exe");
    public static string? RcPath => CombinePath(GetLatestWindowsSdkBin(), "rc.exe");
    public static string? VcpkgPath => GetVcpkgExe();
    public static string? NinjaPath => GetNinjaExe();
    public static string? NugetPath => GetNugetExe();
    public static string? ClangFormatPath => GetClangFormatExe();

    private static string? GetClExe()
    {
        var onPath = FindExeOnPath("cl.exe");

        if (onPath != null)
            return onPath;

        var bundled = CombinePath(LatestMSVCVersionPath(), "bin", "Hostx64", "x64", "cl.exe");

        if (File.Exists(bundled))
            return bundled;

        return null;
    }

    private static string? GetClangFormatExe()
    {
        var onPath = FindExeOnPath("clang-format.exe");

        if (onPath != null)
            return onPath;

        var bundled = CombinePath(InstallPath, "VC", "Tools", "Llvm", "x64", "bin", "clang-format.exe");

        if (File.Exists(bundled))
            return bundled;

        return null;
    }

    private static string? GetVcpkgExe()
    {
        var root = Environment.GetEnvironmentVariable("VCPKG_ROOT");

        if (!string.IsNullOrWhiteSpace(root))
        {
            var envPath = Path.Combine(root, "vcpkg.exe");
            if (File.Exists(envPath))
                return envPath;
        }

        var onPath = FindExeOnPath("vcpkg.exe");

        if (onPath != null)
            return onPath;

        var bundled = CombinePath(InstallPath, "VC", "vcpkg", "vcpkg.exe");

        if (File.Exists(bundled))
            return bundled;

        return null;
    }

    private static string? GetNinjaExe()
    {
        var onPath = FindExeOnPath("ninja.exe");

        if (onPath != null)
            return onPath;

        var bundled = CombinePath(InstallPath, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "Ninja", "ninja.exe");

        if (File.Exists(bundled))
            return bundled;

        return null;
    }

    private static string? GetNugetExe()
    {
        var onPath = FindExeOnPath("nuget.exe");

        if (onPath != null)
            return onPath;

        return null;
    }

    public static string? LatestMSVCVersionPath()
    {
        if (InstallPath is null) return null;

        var vcRoot = Path.Combine(InstallPath, "VC", "Tools", "MSVC");
        if (!Directory.Exists(vcRoot)) return null;

        return Directory.GetDirectories(vcRoot)
                        .OrderByDescending(Path.GetFileName)
                        .FirstOrDefault();
    }

    private static string? CombinePath(string? basePath, params string[] parts) =>
        basePath is null ? null : Path.Combine(new[] { basePath }.Concat(parts).ToArray());

    private static IEnumerable<ISetupInstance> EnumerateInstances(this IEnumSetupInstances enumInstances)
    {
        ISetupInstance[] buffer = new ISetupInstance[1];
        int fetched;
        do
        {
            enumInstances.Next(1, buffer, out fetched);
            if (fetched > 0)
                yield return buffer[0];
        } while (fetched > 0);
    }

    public static async Task<int> Build(Project.BuildConfiguration config)
    {
        if (!Directory.Exists(Project.Paths.Build))
            Directory.CreateDirectory(Project.Paths.Build);

        if (await Generate() != 0)
            throw new InvalidOperationException("Generation failed");

        if (string.IsNullOrWhiteSpace(MSBuildPath))
            throw new InvalidOperationException("MSBuild.exe not found");

        var exe = Project.Exe.MSBuild;
        exe.ArgumentList.Add("-nologo");
        exe.ArgumentList.Add("-v:minimal");
        exe.ArgumentList.Add($"/p:Configuration={(config == Project.BuildConfiguration.Debug ? "Debug" : "Release")}");
        exe.ArgumentList.Add("/p:Platform=x64");
        exe.WorkingDirectory = Project.Paths.Build;
        var exitCode = await App.Run(exe); ;
        Console.Error.WriteLine();

        return exitCode;
    }

    public static int Clean()
    {
        if (!Directory.Exists(Project.Paths.Build))
            return 1;

        string[] dirs = { "debug", "release", "publish" };

        foreach (string dir in dirs)
        {
            var markedDir = Path.Combine(Project.Paths.Build, dir);

            if (Directory.Exists(markedDir))
                Directory.Delete(markedDir, true);
        }

        string[] files = { Project.Paths.SolutionFile, Project.Paths.ProjectFile };

        foreach (string file in files)
        {
            var markedFile = Path.Combine(Project.Paths.Build, file);

            if (File.Exists(markedFile))
                File.Delete(markedFile);
        }

        return 0;
    }

    // public static async Task<string> GetWindowsSdkExecutablePath()
    // {
    //     var devEnv = await DevEnv;

    //     if (!devEnv.TryGetValue("WindowsSdkVerBinPath", out var sdkPath))
    //         throw new KeyNotFoundException("WindowsSdkVerBinPath not found in developer environment.");

    //     if (!Directory.Exists(sdkPath))
    //         throw new DirectoryNotFoundException($"Windows SDK path does not exist: {sdkPath}");

    //     return Path.Combine(sdkPath, "x64");
    // }

    public static string GetLatestWindowsSdkBin(string arch = "x64")
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows SDK is only available on Windows.");

        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string kitsRoot = Path.Combine(programFilesX86, "Windows Kits", "10", "bin");

        if (!Directory.Exists(kitsRoot))
        {
            // Fallback: try registry
            kitsRoot = GetWindowsSdkRootFromRegistry() ?? throw new DirectoryNotFoundException(
                "Windows Kits not found. Ensure Windows 10 SDK is installed.");
            kitsRoot = Path.Combine(kitsRoot, "bin");
        }

        // Get all version folders (e.g., "10.0.22621.0")
        var versions = Directory.GetDirectories(kitsRoot)
            .Select(Path.GetFileName)
            .Where(v => !string.IsNullOrEmpty(v) && Version.TryParse(v, out _))
            .Select(v => Version.Parse(v!))
            .OrderByDescending(v => v)
            .ToList();

        if (versions.Count == 0)
            throw new DirectoryNotFoundException("No Windows SDK versions found.");

        foreach (var version in versions)
        {
            string binPath = Path.Combine(kitsRoot, version.ToString(), arch);
            if (Directory.Exists(binPath))
                return binPath;
        }

        throw new DirectoryNotFoundException($"No Windows SDK bin path found for architecture {arch}.");
    }

    private static string? GetWindowsSdkRootFromRegistry()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows Kits\Installed Roots");
            return key?.GetValue("KitsRoot10") as string;
        }
        catch
        {
            return null;
        }
    }

    // private static async Task SaveEnvToJson(Dictionary<string, string> env)
    // {
    //     var options = new JsonSerializerOptions
    //     {
    //         WriteIndented = true,
    //         Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    //     };

    //     using var stream = File.Create(Path.Combine(App.Paths.AppLocal, "DevEnv.json"));
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

        await SolutionSerializers.SlnXml.SaveAsync(Project.Paths.SolutionFile, solutionModel, new CancellationToken());

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
        var sourceFiles = Directory.GetFiles(Project.Paths.Src, "*.cpp");
        var moduleFiles = Directory.GetFiles(Project.Paths.Src, "*.ixx");
        var headerFiles = Directory.GetFiles(Project.Paths.Src, "*.h");

        var sources = project.AddItemGroup();

        foreach (var sourceFile in sourceFiles)
            sources.AddItem("ClCompile", Path.GetRelativePath(Project.Paths.Build, sourceFile).Replace('\\', '/'));

        foreach (var moduleFile in moduleFiles)
            sources.AddItem("ClCompile", Path.GetRelativePath(Project.Paths.Build, moduleFile).Replace('\\', '/'));

        foreach (var headerFile in headerFiles)
            sources.AddItem("ClInclude", Path.GetRelativePath(Project.Paths.Build, headerFile).Replace('\\', '/'));

        project.Save(Project.Paths.ProjectFile);

        Print.Err("Generation successful.", ConsoleColor.Green);

        return 0;
    }

    private static async Task FormatFileAsync(string file, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "clang-format.exe",
                Arguments = $"-i \"{file}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        try
        {
            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var error = await stderrTask;

            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new Exception(error.Trim());
            }
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            throw;
        }
    }

    public static async Task FormatAsync(CancellationToken ct = default)
    {
        var extensions = new[] { ".c", ".cpp", ".cxx", ".h", ".hpp", ".hxx", ".ixx" };

        var files = Directory.GetFiles(Project.Paths.Src, "*.*", SearchOption.AllDirectories)
                             .Where(f => extensions.Contains(Path.GetExtension(f)))
                             .ToArray();

        if (files.Length == 0)
            return;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        };

        try
        {
            await Parallel.ForEachAsync(files, options, async (file, token) =>
            {
                try
                {
                    await FormatFileAsync(file, token);

                    await ConsoleLock.WaitAsync(token);

                    try
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✓ {file}");
                    }
                    finally
                    {
                        Console.ResetColor();
                        ConsoleLock.Release();
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await ConsoleLock.WaitAsync(token);

                    try
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Error.WriteLine($"Error formatting {file}: {ex.Message}");
                    }
                    finally
                    {
                        Console.ResetColor();
                        ConsoleLock.Release();
                    }
                }
            });
        }
        finally
        {
            Console.ResetColor();
        }
    }
}
