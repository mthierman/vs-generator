using System.CommandLine;
using System.Diagnostics;

public class CLI
{
    public static int parse_args(string[]? args)
    {
        RootCommand root_command = new("vs-generator v0.0.0");
        Console.WriteLine(root_command.Description);

        Command gen = new("gen", "Generate build files") { };
        root_command.Subcommands.Add(gen);

        gen.SetAction(async parseResult =>
        {
            await MSBuild.generate_project();
        });

        Command build = new("build", "Build project") { };
        root_command.Subcommands.Add(build);

        build.SetAction(async parseResult =>
        {
            var build_dir = Path.Combine(Environment.CurrentDirectory, "build");
            if (!Directory.Exists(build_dir))
            {
                Directory.CreateDirectory(build_dir);
            }
            Console.WriteLine($"Building {build_dir}");

            Process.Start("msbuild");
        });

        return root_command.Parse(args!).Invoke();
    }
}
