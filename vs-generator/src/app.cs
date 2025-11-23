using System.Reflection;

public partial class App
{
    public static string src_dir { get; } = Path.Combine(Environment.CurrentDirectory, "src");
    public static string build_dir { get; } = Path.Combine(Environment.CurrentDirectory, "build");
    public static string version { get; } = Assembly.GetExecutingAssembly()
                  .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                  .InformationalVersion ?? string.Empty;

    public enum ExitCode : int
    {
        Success = 0,
        GeneralError = 1,
    }

    public int run(string[] args)
    {
        App.parse_args(args);
        return root_command.Parse(args).Invoke();
    }
}
