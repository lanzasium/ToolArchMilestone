using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace ToolArchMilestone.Core.Services
{
    public interface ISettingsService
    {
        Task<string?> GetSettingAsync(string key);
        Task SaveSettingAsync(string key, string value);
    }

    public class LocalSettingsService : ISettingsService
    {
        private readonly string _settingsPath;

        public LocalSettingsService()
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(folder, "ToolArchMilestone");
            Directory.CreateDirectory(appFolder);
            _settingsPath = Path.Combine(appFolder, "settings.json");
        }

        public async Task<string?> GetSettingAsync(string key)
        {
            if (!File.Exists(_settingsPath)) return null;
            try
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                var dict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json);
                if (dict != null && dict.TryGetValue(key, out var val)) return val;
            }
            catch { }
            return null;
        }

        public async Task SaveSettingAsync(string key, string value)
        {
            System.Collections.Generic.Dictionary<string, string> dict = null;
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_settingsPath);
                    dict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(json);
                }
                catch { }
            }

            if (dict == null) dict = new System.Collections.Generic.Dictionary<string, string>();

            dict[key] = value;

            var options = new JsonSerializerOptions { WriteIndented = true };
            var output = JsonSerializer.Serialize(dict, options);
            await File.WriteAllTextAsync(_settingsPath, output);
        }
    }
}
