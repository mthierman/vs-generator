using System.Reflection;

namespace cxx;

public static class App
{
    public static string Version { get; } = Assembly.GetExecutingAssembly()
              .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
              .InformationalVersion ?? "0.0.0";

    public static int Run(string[] args)
    {
        return CommandLine.RootCommand.Parse(args).Invoke();
    }
}
