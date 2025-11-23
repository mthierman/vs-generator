public class Clangd
{
    public static void generate()
    {
        // ----- 16. Generate compile_commands.json -----
        //var compileCommands = new List<Dictionary<string, string>>();

        //foreach (var config in configurations)
        //{
        //    foreach (var platform in platforms)
        //    {
        //        string includeFlags = $"/I$(ProjectDir)"; // Add more include dirs as needed
        //        string defines = preprocessor;            // from your ClCompile metadata
        //        string languageStandard = "/std:c++23";

        //        var allSourceFiles = Directory.GetFiles(src_dir, "*.*")
        //                                      .Where(f => f.EndsWith(".cpp") || f.EndsWith(".ixx"));

        //        foreach (var file in allSourceFiles)
        //        {
        //            var relativePath = Path.GetRelativePath(build_dir, file).Replace('\\', '/');

        //            string command = $"cl.exe /c {languageStandard} {includeFlags} /D{defines} \"{relativePath}\"";

        //            compileCommands.Add(new Dictionary<string, string>
        //            {
        //                ["directory"] = Path.GetFullPath(build_dir),
        //                ["command"] = command,
        //                ["file"] = Path.GetFullPath(file)
        //            });
        //        }
        //    }
        //}

        //var options = new JsonSerializerOptions { WriteIndented = true };
        //string json = JsonSerializer.Serialize(compileCommands, options);

        //File.WriteAllText(Path.Combine(build_dir, "compile_commands.json"), json);
        //Console.WriteLine("compile_commands.json generated.");
    }
}
