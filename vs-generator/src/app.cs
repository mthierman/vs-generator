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

    public int run(string[] args)
    {
        RootCommand root_command = new($"vs-generator {version}");
        Command gen = new("gen", "Generate build") { };
        root_command.Subcommands.Add(gen);
        gen.SetAction(async parseResult =>
        {
            return (await MSBuild.generate()) ? 0 : 1;
        });

        Command build = new("build", "Build debug") { };
        root_command.Subcommands.Add(build);
        build.SetAction(async parseResult =>
        {
            return (await MSBuild.generate() && MSBuild.build(MSBuild.BuildConfiguration.Debug)) ? 0 : 1;
        });

        Command release = new("release", "Build release") { };
        root_command.Subcommands.Add(release);
        release.SetAction(async parseResult =>
        {
            return (await MSBuild.generate() && MSBuild.build(MSBuild.BuildConfiguration.Release)) ? 0 : 1;
        });

        Command clean = new("clean", "Clean build") { };
        root_command.Subcommands.Add(clean);
        clean.SetAction(async parseResult =>
        {
            Directory.Delete(build_dir, true);
            return 0;
        });

        return root_command.Parse(args).Invoke();
    }
}
