using System.IO;
using System.Text.Json;
using ZhifaRemote.Models;

namespace ZhifaRemote.Services;

public sealed class AppSettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "纸伐局域网远控");

    private static readonly string SettingsFile = Path.Combine(SettingsDirectory, "appsettings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile))
                    ?? new AppSettings();
            }
        }
        catch
        {
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}
