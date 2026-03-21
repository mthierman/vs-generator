public static class Print
{
    public static void Out()
    {
        Console.ResetColor();
        Console.Out.WriteLine();
    }

    public static void Out(string message)
    {
        Console.Out.WriteLine(message);
        Console.ResetColor();
    }

    public static void Out(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Out.WriteLine(message);
        Console.ResetColor();
    }

    public static void Err()
    {
        Console.ResetColor();
        Console.Error.WriteLine();
    }

    public static void Err(string message)
    {
        Console.Error.WriteLine(message);
        Console.ResetColor();
    }

    public static void Err(string message, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.Error.WriteLine(message);
        Console.ResetColor();
    }
}
