using System.Text.Json;
using SleepOS.Core;

﻿namespace SleepOS.Settings;

public interface IAppSettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private readonly string _filePath;

    public JsonAppSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SleepOS", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? Environment.CurrentDirectory);
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(_filePath);
        return string.IsNullOrWhiteSpace(json)
            ? new AppSettings()
            : JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}