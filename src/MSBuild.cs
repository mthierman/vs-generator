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
}
