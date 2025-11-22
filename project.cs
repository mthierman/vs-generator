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
var extensionSettings = project.AddImportGroup();
extensionSettings.Label = "ExtensionSettings";

// ----- 7. Shared ImportGroup -----
var sharedImports = project.AddImportGroup();
sharedImports.Label = "Shared";

// ----- 8. Per-configuration PropertySheets -----
foreach (var config in configurations)
{
    foreach (var platform in platforms)
    {
        var pg = project.AddImportGroup();
        pg.Label = "PropertySheets";
        pg.Condition = $"'$(Configuration)|$(Platform)'=='{config}|{platform}'";

        var import = pg.AddImport(@"$(UserRootDir)\Microsoft.Cpp.$(Platform).user.props");
        import.Condition = $"exists('$(UserRootDir)\\Microsoft.Cpp.$(Platform).user.props')";
        import.Label = "LocalAppDataPlatform";
    }
}


project.Save("build/app.vcxproj");
