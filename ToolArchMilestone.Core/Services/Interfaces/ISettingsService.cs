using System.Threading.Tasks;

namespace ToolArchMilestone.Core.Services.Interfaces
{
    public interface ISettingsService
    {
        Task InitializeAsync();
        Task<T?> GetSettingAsync<T>(string key);
        Task SaveSettingAsync<T>(string key, T value);
        
        // Helper for string specific legacy calls if needed, or generic covers it
        Task<string?> GetSettingAsync(string key);
        Task SaveSettingAsync(string key, string value);
    }
}
