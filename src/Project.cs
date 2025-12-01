using System.Text.Json;

namespace CXX;

public static class Project
{
    public sealed class Config
    {
        public string name { get; set; } = $"blank-project";
        public string version { get; set; } = "0.0.0";
    }

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static Config Load(string path)
    {
        if (!File.Exists(path))
        {
            var config = new Config();
            Save(config, path);
            return config;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Config>(json, Options)
               ?? new Config();
    }

    public static void Save(Config config, string path)
    {
        var json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(path, json);
    }
}
