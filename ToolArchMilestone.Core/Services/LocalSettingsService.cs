using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Core.Services
{
    public class LocalSettingsService : ISettingsService
    {
        private const string SettingsFileName = "local_settings.json";
        private readonly string _settingsFolder;
        private readonly string _settingsFile;
        private Dictionary<string, object> _settings;

        public LocalSettingsService()
        {
            _settingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolArchMilestone");
            _settingsFile = Path.Combine(_settingsFolder, SettingsFileName);
            _settings = new Dictionary<string, object>();
        }

        public async Task InitializeAsync()
        {
            if (!Directory.Exists(_settingsFolder))
            {
                Directory.CreateDirectory(_settingsFolder);
            }

            if (File.Exists(_settingsFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_settingsFile);
                    _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
                }
                catch
                {
                    _settings = new Dictionary<string, object>();
                }
            }
        }

        public async Task<T?> GetSettingAsync<T>(string key)
        {
            if (_settings.TryGetValue(key, out var obj))
            {
                if (obj is JsonElement element)
                {
                    return element.Deserialize<T>();
                }
                return (T)obj;
            }
            return default;
        }

        public async Task SaveSettingAsync<T>(string key, T value)
        {
            if (value == null)
            {
                _settings.Remove(key);
            }
            else
            {
                _settings[key] = value;
            }
            
            await SaveToFileAsync();
        }

        public async Task<string?> GetSettingAsync(string key)
        {
            return await GetSettingAsync<string>(key);
        }

        public async Task SaveSettingAsync(string key, string value)
        {
            await SaveSettingAsync<string>(key, value);
        }

        private async Task SaveToFileAsync()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_settings, options);
            await File.WriteAllTextAsync(_settingsFile, json);
        }
    }
}
