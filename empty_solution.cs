#:package Microsoft.VisualStudio.SolutionPersistence@1.0.52
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

var solution = new SolutionModel();

solution.AddProject("app.vcxproj");
solution.AddPlatform("x64");
solution.AddPlatform("x86");

await SolutionSerializers.SlnXml.SaveAsync("build/app.slnx", solution, new CancellationToken());
