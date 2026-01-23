using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Services
{
    public class WinUIFilePickerService : IFilePickerService
    {
        public async Task<string?> PickSingleFileAsync(string[] fileTypes)
        {
            var picker = new FileOpenPicker();

            // WinUI 3 Window Handle hack for pickers
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

            foreach (var type in fileTypes)
            {
                picker.FileTypeFilter.Add(type);
            }

            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
    }
}
