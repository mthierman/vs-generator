#:package Microsoft.Build@18.0.2
using Microsoft.Build.Construction;

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

        cl_compile.AddMetadata("WarningLevel", "Level3", false);
        cl_compile.AddMetadata("SDLCheck", "true", false);
        cl_compile.AddMetadata("ConformanceMode", "true", false);
        cl_compile.AddMetadata("LanguageStandard", "stdcpp23", false);

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

project.Save("build/app.vcxproj");
