using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using ToolArchMilestone.Core.ViewModels;
using ToolArchMilestone.Services;

namespace ToolArchMilestone
{
    public sealed partial class LaunchPage : Page
    {
        public LaunchViewModel ViewModel { get; }

        public LaunchPage()
        {
            this.InitializeComponent();
            var filePicker = new WinUIFilePickerService();
            ViewModel = new LaunchViewModel(App.JobManager!, filePicker);
        }

        private void DropArea_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }

        private async void DropArea_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0)
                {
                    var storageFile = items[0] as Windows.Storage.StorageFile;
                    if (storageFile != null && storageFile.FileType == ".txt")
                    {
                        var content = await Windows.Storage.FileIO.ReadTextAsync(storageFile);
                        ViewModel.IntervalsText = content;
                    }
                }
            }
        }

        private void StartPicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            ViewModel.UpdateStartString();
        }

        private void StartTime_TimeChanged(object sender, TimePickerValueChangedEventArgs e)
        {
            ViewModel.UpdateStartString();
        }

        private void EndPicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            ViewModel.UpdateEndString();
        }

        private void EndTime_TimeChanged(object sender, TimePickerValueChangedEventArgs e)
        {
            ViewModel.UpdateEndString();
        }
    }
}
