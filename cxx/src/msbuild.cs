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

    private static async Task GenerateSolution()
    {
        var solution_model = new SolutionModel();

        solution_model.AddPlatform("x64");
        solution_model.AddPlatform("x86");

        var solution_project = solution_model.AddProject("app.vcxproj");

        solution_project.Id = Guid.NewGuid();

        await SolutionSerializers.SlnXml.SaveAsync(Paths.solution_file, solution_model, new CancellationToken());
    }

    public static async Task<bool> Generate()
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
        vcpkg.AddProperty("VcpkgUseStatic", "true");
        vcpkg.AddProperty("VcpkgUseMD", "true");

        // ----- 15. Add sources from "src" folder -----
        var source_files = Directory.GetFiles(Paths.src, "*.cpp");
        var module_files = Directory.GetFiles(Paths.src, "*.ixx");
        var header_files = Directory.GetFiles(Paths.src, "*.h");

        var sources = project.AddItemGroup();

        foreach (var source_file in source_files)
            sources.AddItem("ClCompile", Path.GetRelativePath(Paths.build, source_file).Replace('\\', '/'));

        foreach (var module_file in module_files)
            sources.AddItem("ClCompile", Path.GetRelativePath(Paths.build, module_file).Replace('\\', '/'));

        foreach (var header_file in header_files)
            sources.AddItem("ClInclude", Path.GetRelativePath(Paths.build, header_file).Replace('\\', '/'));

        project.Save(Paths.project_file);

        return true;
    }

    public static async Task<bool> Build(BuildConfiguration config)
    {
        Directory.CreateDirectory(Paths.build);

        if (!await Generate())
            return false;

        if (string.IsNullOrWhiteSpace(Paths.MSBuild))
            throw new InvalidOperationException("MSBuild path not set.");

        var args = $"-nologo -v:minimal /p:Configuration={(config == BuildConfiguration.Debug ? "Debug" : "Release")} /p:Platform=x64";
        var process = Process.Start(new ProcessStartInfo(Paths.MSBuild, args) { WorkingDirectory = Paths.build }) ?? throw new InvalidOperationException("Failed to start MSBuild");
        process.WaitForExit();

        Console.Error.WriteLine();

        return process.ExitCode == 0;
    }

    public static bool Clean()
    {
        if (!Directory.Exists(Paths.build))
            return false;

        Directory.Delete(Paths.build, true);

        return true;
    }
}
