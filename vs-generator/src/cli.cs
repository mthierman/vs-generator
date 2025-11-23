using System.CommandLine;

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

        return root_command.Parse(args!).Invoke();
    }
}
