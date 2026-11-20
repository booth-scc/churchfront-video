using System.Text.Json;

namespace CueVideo.Core;

/// <summary>
/// Keeps config.json and the running app in step. A copy of the last setup that parsed
/// cleanly sits beside it, so a typo in the file never leaves the app with nothing.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public ConfigStore(string? directory = null)
    {
        directory ??= Environment.GetEnvironmentVariable("CUEVIDEO_CONFIG_DIR") is { Length: > 0 } custom
            ? custom
            : null;

        Directory = directory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CueVideo");
        Path = System.IO.Path.Combine(Directory, "config.json");
        LastGoodPath = System.IO.Path.Combine(Directory, "config.last-good.json");
    }

    public string Directory { get; }
    public string Path { get; }
    public string LastGoodPath { get; }

    public CueConfig Config { get; private set; } = new();
    public string? FileError { get; private set; }
    public bool UsedLastGood { get; private set; }
    public bool CreatedDefaults { get; private set; }

    public void Load()
    {
        FileError = null;
        UsedLastGood = false;
        CreatedDefaults = false;

        if (File.Exists(Path))
        {
            try
            {
                Config = Parse(File.ReadAllText(Path));
                SaveLastGood(Config);
                return;
            }
            catch (Exception ex)
            {
                FileError = $"config.json could not be read: {ex.Message}";
            }
        }

        if (File.Exists(LastGoodPath))
        {
            try
            {
                Config = Parse(File.ReadAllText(LastGoodPath));
                UsedLastGood = true;
                return;
            }
            catch (Exception ex)
            {
                FileError = $"{FileError} Last good copy also failed: {ex.Message}";
            }
        }

        Config = CueConfig.BoothDefaults();
        CreatedDefaults = true;
        Save();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var json = JsonSerializer.Serialize(Config, Options);

            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, Path, true);
            SaveLastGood(Config);
        }
        catch (Exception ex)
        {
            FileError = $"config.json could not be written: {ex.Message}";
        }
    }

    private void SaveLastGood(CueConfig config)
    {
        try
        {
            File.WriteAllText(LastGoodPath, JsonSerializer.Serialize(config, Options));
        }
        catch (Exception)
        {
            // A missing safety copy is not worth failing the load over.
        }
    }

    private static CueConfig Parse(string json) =>
        JsonSerializer.Deserialize<CueConfig>(json, Options) ?? throw new InvalidDataException("empty config");
}
