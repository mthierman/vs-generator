using System.CommandLine;
using System.Reflection;

public class App
{
    public enum ExitCode : int
    {
        Success = 0,
        GeneralError = 1,
    }
    public static string src_dir { get; } = Path.Combine(Environment.CurrentDirectory, "src");
    public static string build_dir { get; } = Path.Combine(Environment.CurrentDirectory, "build");
    public static string version { get; } = Assembly.GetExecutingAssembly()
                  .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                  .InformationalVersion ?? string.Empty;
    private static RootCommand root_command { get; } = new RootCommand($"vs-generator {version}");
    private static Dictionary<string, Command> sub_command = new Dictionary<string, Command>
    {
        ["gen"] = new Command("gen", "Generate build"),
        ["build"] = new Command("build", "Build debug"),
        ["release"] = new Command("release", "Build release"),
        ["clean"] = new Command("clean", "Clean build")
    };

    public int run(string[] args)
    {
        foreach (var command in sub_command.Values)
        {
            root_command.Subcommands.Add(command);
        }

        sub_command["gen"].SetAction(async parseResult =>
        {
            return (await MSBuild.generate()) ? 0 : 1;
        });


        sub_command["build"].SetAction(async parseResult =>
        {
            return (await MSBuild.generate() && MSBuild.build(MSBuild.BuildConfiguration.Debug)) ? 0 : 1;
        });

        sub_command["release"].SetAction(async parseResult =>
        {
            return (await MSBuild.generate() && MSBuild.build(MSBuild.BuildConfiguration.Release)) ? 0 : 1;
        });

        sub_command["clean"].SetAction(async parseResult =>
        {
            if (Directory.Exists(build_dir))
            { Directory.Delete(build_dir, true); }

            return 0;
        });

        return root_command.Parse(args).Invoke();
    }
}
