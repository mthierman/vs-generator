#:package Microsoft.VisualStudio.SolutionPersistence@1.0.52
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

var solution = new SolutionModel();

var project = solution.AddProject("app.vcxproj");
project.Id = Guid.NewGuid();
solution.AddPlatform("x64");
solution.AddPlatform("x86");

await SolutionSerializers.SlnXml.SaveAsync("build/app.slnx", solution, new CancellationToken());
