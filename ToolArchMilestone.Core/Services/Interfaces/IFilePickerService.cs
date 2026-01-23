using System;
using System.Threading.Tasks;

namespace ToolArchMilestone.Core.Services.Interfaces
{
    public interface IFilePickerService
    {
        Task<string?> PickSingleFileAsync(string[] fileTypes);
    }
}
