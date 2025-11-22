#:package Microsoft.Build@18.0.2
using Microsoft.Build.Construction;

var project = ProjectRootElement.Create();
project.DefaultTargets = "Build";
project.ToolsVersion = null;
var project_configurations = project.AddItemGroup();
project_configurations.Label = "ProjectConfigurations";

string[] configurations = { "Debug", "Release" };
string[] platforms = { "Win32", "x64" };

foreach (var config in configurations)
{
    foreach (var platform in platforms)
    {
        var item = project_configurations.AddItem("ProjectConfiguration", $"{config}|{platform}");
        item.AddMetadata("Configuration", config);
        item.AddMetadata("Platform", platform);
    }
}

project.Save("build/app.vcxproj");
