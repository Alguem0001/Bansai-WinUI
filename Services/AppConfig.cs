using System.Text.Json;

namespace BonsaiWinUI.Services;

public sealed class AppConfig
{
    public string LlamaBin { get; set; } = @"C:\Users\geron\OneDrive\Desktop\AI\Bansai Llama.cpp\llama.cpp\build\bin";
    public string ModelsDir { get; set; } = @"C:\Users\geron\OneDrive\Desktop\AI\Bansai Llama.cpp\models";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public int ContextSize { get; set; } = 8192;
    public int GpuLayers { get; set; } = 99;
    public bool EnableTools { get; set; } = true;
    public bool EnableJinja { get; set; } = true;
    public bool EnableVision { get; set; } = true;
    public string ExtraArgs { get; set; } = "";
    public string? LastModelPath { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string ConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BonsaiWinUI",
            "config.json");

    public static AppConfig Load()
    {
        try
        {
            var path = ConfigPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
            }
        }
        catch
        {
            // ignore corrupt config
        }
        return new AppConfig();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, JsonOpts);
        File.WriteAllText(ConfigPath, json);
    }
}

