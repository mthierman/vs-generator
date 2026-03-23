using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CXX;

public static class Cps
{
    private const string DefaultCpsVersion = "0.14.1";

    public sealed class Platform
    {
        [JsonPropertyName("c_runtime_vendor")]
        public string? CRuntimeVendor { get; set; }

        [JsonPropertyName("c_runtime_version")]
        public string? CRuntimeVersion { get; set; }

        [JsonPropertyName("clr_vendor")]
        public string? ClrVendor { get; set; }

        [JsonPropertyName("clr_version")]
        public string? ClrVersion { get; set; }

        [JsonPropertyName("cpp_runtime_vendor")]
        public string? CppRuntimeVendor { get; set; }

        [JsonPropertyName("cpp_runtime_version")]
        public string? CppRuntimeVersion { get; set; }

        [JsonPropertyName("isa")]
        public string? Isa { get; set; }

        [JsonPropertyName("jvm_vendor")]
        public string? JvmVendor { get; set; }

        [JsonPropertyName("jvm_version")]
        public string? JvmVersion { get; set; }

        [JsonPropertyName("kernel")]
        public string? Kernel { get; set; }

        [JsonPropertyName("kernel_version")]
        public string? KernelVersion { get; set; }
    }

    public sealed class Requirement
    {
        [JsonPropertyName("components")]
        public List<string>? Components { get; set; }

        [JsonPropertyName("hints")]
        public List<string>? Hints { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    [JsonConverter(typeof(LanguageStringListJsonConverter))]
    public sealed class LanguageStringList
    {
        public List<string>? Values { get; init; }
        public Dictionary<string, List<string>>? ByLanguage { get; init; }

        public static LanguageStringList FromValues(List<string> values) => new() { Values = values };
        public static LanguageStringList FromByLanguage(Dictionary<string, List<string>> byLanguage) => new() { ByLanguage = byLanguage };
    }

    public class Configuration
    {
        [JsonPropertyName("compile_features")]
        public List<string>? CompileFeatures { get; set; }

        [JsonPropertyName("compile_flags")]
        public LanguageStringList? CompileFlags { get; set; }

        [JsonPropertyName("compile_requires")]
        public List<string>? CompileRequires { get; set; }

        [JsonPropertyName("cpp_module_metadata")]
        public string? CppModuleMetadata { get; set; }

        [JsonPropertyName("definitions")]
        public Dictionary<string, Dictionary<string, string?>>? Definitions { get; set; }

        [JsonPropertyName("dyld_requires")]
        public List<string>? DyldRequires { get; set; }

        [JsonPropertyName("includes")]
        public LanguageStringList? Includes { get; set; }

        [JsonPropertyName("link_features")]
        public List<string>? LinkFeatures { get; set; }

        [JsonPropertyName("link_flags")]
        public List<string>? LinkFlags { get; set; }

        [JsonPropertyName("link_languages")]
        public List<string>? LinkLanguages { get; set; }

        [JsonPropertyName("link_libraries")]
        public List<string>? LinkLibraries { get; set; }

        [JsonPropertyName("link_location")]
        public string? LinkLocation { get; set; }

        [JsonPropertyName("link_requires")]
        public List<string>? LinkRequires { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("requires")]
        public List<string>? RequiredComponents { get; set; }
    }

    public sealed class Component : Configuration
    {
        [JsonPropertyName("configurations")]
        public Dictionary<string, Configuration>? Configurations { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    public sealed class Package
    {
        [JsonPropertyName("compat_version")]
        public string? CompatVersion { get; set; }

        [JsonPropertyName("configuration")]
        public string? ConfigurationName { get; set; }

        [JsonPropertyName("configurations")]
        public List<string>? Configurations { get; set; }

        [JsonPropertyName("cps_path")]
        public string? CpsPath { get; set; }

        [JsonPropertyName("cps_version")]
        public string? CpsVersion { get; set; }

        [JsonPropertyName("default_components")]
        public List<string>? DefaultComponents { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("platform")]
        public Platform? Platform { get; set; }

        [JsonPropertyName("prefix")]
        public string? Prefix { get; set; }

        [JsonPropertyName("requires")]
        public Dictionary<string, Requirement?>? RequiredPackages { get; set; }

        [JsonPropertyName("components")]
        public Dictionary<string, Component>? Components { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("version_schema")]
        public string? VersionSchema { get; set; }
    }

    public static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();
    public static readonly JsonSerializerOptions WriteOptions = CreateWriteOptions();

    public static Package Parse(string json, string? filePath = null)
    {
        try
        {
            var package = JsonSerializer.Deserialize<Package>(json, ReadOptions)
                ?? throw new InvalidDataException("failed to parse CPS package");

            var configurationSpecificFile = filePath is not null && Path.GetFileName(filePath).Contains("@");
            ValidatePackage(package, configurationSpecificFile);
            return package;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }
    }

    public static Package ParseFile(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path), path);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"failed to read `{path}`: {exception.Message}", exception);
        }
    }

    public static Package ParseMinimalFile(string path)
    {
        try
        {
            return ParseMinimal(File.ReadAllText(path), path);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"failed to read `{path}`: {exception.Message}", exception);
        }
    }

    public static Package ParseMinimal(string json, string? filePath = null)
    {
        try
        {
            return CreateMinimalPackage(Parse(json, filePath));
        }
        catch (InvalidDataException strictError)
        {
            try
            {
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                return CreateMinimalPackage(document.RootElement);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                throw new InvalidDataException(strictError.Message, strictError);
            }
        }
    }

    public static string Serialize(Package package) => JsonSerializer.Serialize(package, WriteOptions);

    private static JsonSerializerOptions CreateReadOptions()
    {
        return new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
    }

    private static JsonSerializerOptions CreateWriteOptions()
    {
        return new JsonSerializerOptions(ReadOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };
    }

    private static void ValidateConfiguration(Configuration configuration)
    {
        configuration.LinkLanguages ??= new List<string> { "c" };
    }

    private static Package CreateMinimalPackage(Package package)
    {
        var name = package.Name ?? "sample";
        var components = new Dictionary<string, Component>();

        foreach (var (componentName, component) in package.Components ?? [])
        {
            var minimalComponent = CreateMinimalComponent(component);
            if (minimalComponent is not null)
                components[componentName] = minimalComponent;
        }

        if (components.Count == 0)
        {
            var fallbackName = package.DefaultComponents?.FirstOrDefault() ?? name;
            components[fallbackName] = new Component { Type = "interface" };
        }

        var minimal = new Package
        {
            Name = name,
            CpsVersion = package.CpsVersion ?? DefaultCpsVersion,
            Components = components
        };

        if (!string.IsNullOrWhiteSpace(package.CpsPath))
            minimal.CpsPath = package.CpsPath;
        else
            minimal.Prefix = !string.IsNullOrWhiteSpace(package.Prefix) ? package.Prefix : $"/opt/{name}";

        return minimal;
    }

    private static Package CreateMinimalPackage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("CPS root must be an object");

        var name = GetOptionalString(root, "name") ?? "sample";
        var components = new Dictionary<string, Component>();

        if (root.TryGetProperty("components", out var componentMap) && componentMap.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in componentMap.EnumerateObject())
            {
                var minimalComponent = CreateMinimalComponent(property.Value);
                if (minimalComponent is not null)
                    components[property.Name] = minimalComponent;
            }
        }

        if (components.Count == 0)
        {
            var fallbackName = GetFirstArrayString(root, "default_components") ?? name;
            components[fallbackName] = new Component { Type = "interface" };
        }

        var minimal = new Package
        {
            Name = name,
            CpsVersion = GetOptionalString(root, "cps_version") ?? DefaultCpsVersion,
            Components = components
        };

        var cpsPath = GetOptionalString(root, "cps_path");
        var prefix = GetOptionalString(root, "prefix");

        if (!string.IsNullOrWhiteSpace(cpsPath))
            minimal.CpsPath = cpsPath;
        else
            minimal.Prefix = !string.IsNullOrWhiteSpace(prefix) ? prefix : $"/opt/{name}";

        return minimal;
    }

    private static Component? CreateMinimalComponent(Component component)
    {
        var type = component.Type ?? "interface";
        if (string.Equals(type, "interface", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "symbolic", StringComparison.OrdinalIgnoreCase))
        {
            return new Component { Type = type };
        }

        var location = component.Location;
        var linkLocation = component.LinkLocation;

        if (location is null && component.Configurations is not null)
        {
            foreach (var configuration in component.Configurations.Values)
            {
                location ??= configuration.Location;
                linkLocation ??= configuration.LinkLocation;

                if (location is not null)
                    break;
            }
        }

        if (location is null)
            return null;

        return new Component
        {
            Type = type,
            Location = location,
            LinkLocation = linkLocation
        };
    }

    private static Component? CreateMinimalComponent(JsonElement component)
    {
        if (component.ValueKind != JsonValueKind.Object)
            return null;

        var type = GetOptionalString(component, "type") ?? "interface";
        if (string.Equals(type, "interface", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "symbolic", StringComparison.OrdinalIgnoreCase))
        {
            return new Component { Type = type };
        }

        var location = GetOptionalString(component, "location");
        var linkLocation = GetOptionalString(component, "link_location");

        if (location is null
            && component.TryGetProperty("configurations", out var configurations)
            && configurations.ValueKind == JsonValueKind.Object)
        {
            foreach (var configuration in configurations.EnumerateObject())
            {
                if (configuration.Value.ValueKind != JsonValueKind.Object)
                    continue;

                location ??= GetOptionalString(configuration.Value, "location");
                linkLocation ??= GetOptionalString(configuration.Value, "link_location");

                if (location is not null)
                    break;
            }
        }

        if (location is null)
            return null;

        return new Component
        {
            Type = type,
            Location = location,
            LinkLocation = linkLocation
        };
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return value.GetString();
    }

    private static string? GetFirstArrayString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                return item.GetString();
        }

        return null;
    }

    private static void ValidateComponent(string name, Component component, bool configurationSpecificPackage)
    {
        ValidateConfiguration(component);

        if (configurationSpecificPackage)
        {
            if (component.Type is not null)
                throw new InvalidDataException($"configuration-specific CPS component `{name}` may not define `type`");

            if (component.Configurations is not null)
                throw new InvalidDataException($"configuration-specific CPS component `{name}` may not define `configurations`");
        }
        else if (component.Type is null)
        {
            throw new InvalidDataException($"component `{name}` is missing required `type`");
        }

        if (component.Configurations is null)
            return;

        foreach (var configuration in component.Configurations.Values)
            ValidateConfiguration(configuration);
    }

    private static void ValidatePackage(Package package, bool configurationSpecificFile = false)
    {
        if (package.Name is null)
            throw new InvalidDataException("package is missing required `name`");

        if (package.Components is null)
            throw new InvalidDataException("package is missing required `components`");

        var configurationSpecificPackage = configurationSpecificFile || package.ConfigurationName is not null;

        if (configurationSpecificPackage && package.ConfigurationName is null)
            throw new InvalidDataException("configuration-specific CPS package is missing required `configuration`");

        if (configurationSpecificPackage)
        {
            if (package.CompatVersion is not null
                || package.Configurations is not null
                || package.CpsPath is not null
                || package.CpsVersion is not null
                || package.DefaultComponents is not null
                || package.Platform is not null
                || package.Prefix is not null
                || package.RequiredPackages is not null
                || package.Version is not null
                || package.VersionSchema is not null)
            {
                throw new InvalidDataException("configuration-specific CPS files may only define `name`, `configuration`, and `components`");
            }
        }
        else
        {
            if (package.CpsVersion is null)
                throw new InvalidDataException("package is missing required `cps_version`");

            if ((package.CpsPath is null) == (package.Prefix is null))
                throw new InvalidDataException("package must define exactly one of `prefix` or `cps_path`");

            package.VersionSchema ??= "simple";

            if (package.CompatVersion is null && package.Version is not null)
                package.CompatVersion = package.Version;
        }

        foreach (var (name, component) in package.Components)
            ValidateComponent(name, component, configurationSpecificPackage);
    }

    private sealed class LanguageStringListJsonConverter : JsonConverter<LanguageStringList>
    {
        public override bool HandleNull => true;

        public override LanguageStringList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.StartArray => LanguageStringList.FromValues(
                    JsonSerializer.Deserialize<List<string>>(ref reader, options)
                    ?? throw new JsonException("expected a string array")),
                JsonTokenType.StartObject => LanguageStringList.FromByLanguage(
                    JsonSerializer.Deserialize<Dictionary<string, List<string>>>(ref reader, options)
                    ?? throw new JsonException("expected a language map")),
                JsonTokenType.Null => null,
                _ => throw new JsonException("expected an array or object")
            };
        }

        public override void Write(Utf8JsonWriter writer, LanguageStringList value, JsonSerializerOptions options)
        {
            if (value.Values is not null)
            {
                JsonSerializer.Serialize(writer, value.Values, options);
                return;
            }

            if (value.ByLanguage is not null)
            {
                JsonSerializer.Serialize(writer, value.ByLanguage, options);
                return;
            }

            writer.WriteNullValue();
        }
    }
}
