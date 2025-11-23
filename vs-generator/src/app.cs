using System.CommandLine;
using System.Diagnostics;
using System.Reflection;

public class App
{
    public static string src_dir { get; } = Path.Combine(Environment.CurrentDirectory, "src");
    public static string build_dir { get; } = Path.Combine(Environment.CurrentDirectory, "build");

    public int run(string[] args)
    {
        if (!Directory.Exists(build_dir))
        {
            Directory.CreateDirectory(build_dir);

            if (!Directory.Exists(build_dir))
            {
                return 1;
            }
        }

        var version = Assembly.GetExecutingAssembly()
                      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                      .InformationalVersion;

        RootCommand root_command = new($"vs-generator {version}");

        Command gen = new("gen", "Generate build") { };
        root_command.Subcommands.Add(gen);
        gen.SetAction(async parseResult =>
        {
            await MSBuild.generate();
        });

        Command build = new("build", "Build debug") { };
        root_command.Subcommands.Add(build);
        build.SetAction(async parseResult =>
        {
            if (await MSBuild.generate())
            {
                using var process = Process.Start(new ProcessStartInfo() { FileName = MSBuild.exe(), WorkingDirectory = build_dir });
                process?.WaitForExit();
            }
        });

        Command release = new("release", "Build release") { };
        root_command.Subcommands.Add(release);
        release.SetAction(async parseResult =>
        {
            if (await MSBuild.generate())
            {
                using var process = Process.Start(new ProcessStartInfo() { FileName = MSBuild.exe(), WorkingDirectory = build_dir, Arguments = "/p:Configuration=Release" });
                process?.WaitForExit();
            }
        });

        return root_command.Parse(args).Invoke();
    }
}
