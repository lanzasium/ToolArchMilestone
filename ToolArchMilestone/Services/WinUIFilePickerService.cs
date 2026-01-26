using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Services
{
    public class WinUIFilePickerService : IFilePickerService
    {
        public async Task<string> PickSingleFileAsync(string[] fileTypes)
        {
            var picker = new FileOpenPicker();
            
            // WinUI 3 Window Handle hack for pickers
            // App.Window is set in App.xaml.cs
            // Explicit cast to object to resolve type conversion error (MainWindow -> FrameworkElement confusion)
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle((object)App.Window!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            
            foreach (var type in fileTypes)
            {
                picker.FileTypeFilter.Add(type);
            }

            var file = await picker.PickSingleFileAsync();
            return file?.Path ?? string.Empty;
        }

        public async Task<string> PickSingleFolderAsync()
        {
            var picker = new FolderPicker();
            
            // Explicit cast to object to resolve type conversion error
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle((object)App.Window!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path ?? string.Empty;
        }
    }
}