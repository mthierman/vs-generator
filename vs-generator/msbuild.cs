using System.Diagnostics;
using Microsoft.Build.Construction;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

public class MSBuild
{
    public enum BuildConfiguration
    {
        Debug,
        Release
    }

    public static class Paths
    {
        public static string? exe;
        public static string base_dir { get; } = Environment.CurrentDirectory;
        public static string src_dir => Path.Combine(base_dir, "src");
        public static string build_dir => Path.Combine(base_dir, "build");
    }

    static MSBuild()
    {
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio\\Installer\\vswhere.exe");

        if (File.Exists(vswhere))
        {
            using var process = Process.Start(new ProcessStartInfo()
            {
                FileName = vswhere,
                Arguments = "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\amd64\\MSBuild.exe",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process != null)
            {
                var output = process?.StandardOutput.ReadToEnd();
                process?.WaitForExit();

                var path = output?.Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries)[0];
                Paths.exe = string.IsNullOrWhiteSpace(path) ? null : path;
            }
        }
    }

    public static async Task<bool> generate()
    {
        if (!Directory.Exists(Paths.src_dir))
            return false;

        var solution_model = new SolutionModel();
        solution_model.AddPlatform("x64");
        solution_model.AddPlatform("x86");

        var solution_project = solution_model.AddProject("app.vcxproj");
        solution_project.Id = Guid.NewGuid();

        await SolutionSerializers.SlnXml.SaveAsync("build/app.slnx", solution_model, new CancellationToken());

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
                group.AddProperty("IntDir", $@"$(SolutionDir)\{config.ToLowerInvariant()}\obj\");
                group.AddProperty("OutDir", $@"$(SolutionDir)\{config.ToLowerInvariant()}\");
            }
        }

        // ----- 4. ProjectConfigurations (must be AFTER config groups) -----
        var project_configurations = project.AddItemGroup();
        project_configurations.Label = "ProjectConfigurations";

        foreach (var config in configurations)
        {
            foreach (var platform in platforms)
            {
                var item = project_configurations.AddItem("ProjectConfiguration", $"{config}|{platform}");
                item.AddMetadata("Configuration", config);
                item.AddMetadata("Platform", platform);
            }
        }

        // ----- 5. Import Microsoft.Cpp.props -----
        project.AddImport(@"$(VCTargetsPath)\Microsoft.Cpp.props");

        // ----- 6. ExtensionSettings ImportGroup -----
        var extension_settings = project.AddImportGroup();
        extension_settings.Label = "ExtensionSettings";

        // ----- 7. Shared ImportGroup -----
        var shared = project.AddImportGroup();
        shared.Label = "Shared";

        // ----- 8. Per-configuration PropertySheets -----
        foreach (var config in configurations)
        {
            foreach (var platform in platforms)
            {
                var property_sheets = project.AddImportGroup();
                property_sheets.Label = "PropertySheets";
                property_sheets.Condition = $"'$(Configuration)|$(Platform)'=='{config}|{platform}'";

                var import = property_sheets.AddImport(@"$(UserRootDir)\Microsoft.Cpp.$(Platform).user.props");
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
                var project_settings = project.AddItemDefinitionGroup();
                project_settings.Condition = $"'$(Configuration)|$(Platform)'=='{config}|{platform}'";

                // ----- ClCompile -----
                var cl_compile = project_settings.AddItemDefinition("ClCompile");

                cl_compile.AddMetadata("WarningLevel", "Level4", false);
                cl_compile.AddMetadata("TreatWarningAsError", "true", false);
                cl_compile.AddMetadata("SDLCheck", "true", false);
                cl_compile.AddMetadata("ConformanceMode", "true", false);
                cl_compile.AddMetadata("LanguageStandard", "stdcpp23", false);
                cl_compile.AddMetadata("LanguageStandard_C", "stdc17", false);
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
                var link = project_settings.AddItemDefinition("Link");
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

        // ----- 15. Add sources from "src" folder -----
        string build_dir = "build";   // where the .vcxproj will be generated
                                      // string root_dir = ".";    // current directory, script runs at project root
        string src_dir = "src";

        if (Directory.Exists(src_dir))
        {
            var source_files = Directory.GetFiles(src_dir, "*.cpp");
            var header_files = Directory.GetFiles(src_dir, "*.h");
            var module_files = Directory.GetFiles(src_dir, "*.ixx");

            if (source_files.Length > 0 || header_files.Length > 0)
            {
                var sources = project.AddItemGroup();

                foreach (var source_file in source_files)
                    sources.AddItem("ClCompile", Path.GetRelativePath(build_dir, source_file).Replace('\\', '/'));

                foreach (var module_file in module_files)
                    sources.AddItem("ClCompile", Path.GetRelativePath(build_dir, module_file).Replace('\\', '/'));

                foreach (var header_file in header_files)
                    sources.AddItem("ClInclude", Path.GetRelativePath(build_dir, header_file).Replace('\\', '/'));
            }
            else
            {
                Console.WriteLine("Warning: 'src' directory exists but contains no .cpp or .h files.");
            }
        }
        else
        {
            Console.WriteLine("Warning: 'src' directory does not exist.");
        }

        project.Save("build/app.vcxproj");

        return true;
    }

    public static async Task<bool> build(BuildConfiguration config)
    {
        if (!Directory.Exists(Paths.build_dir))
            Directory.CreateDirectory(Paths.build_dir);

        if (!await generate())
            return false;

        var start_info = new ProcessStartInfo() { FileName = Paths.exe, WorkingDirectory = Paths.build_dir, Arguments = config == BuildConfiguration.Release ? "/p:Configuration=Release" : string.Empty };
        Process.Start(start_info)?.WaitForExit();

        return true;
    }

    public static bool clean()
    {
        if (!Directory.Exists(Paths.build_dir))
            return false;

        Directory.Delete(Paths.build_dir, true);

        return true;
    }
}
