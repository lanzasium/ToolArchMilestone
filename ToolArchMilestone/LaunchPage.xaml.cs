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
        public LaunchViewModel ViewModel => App.MainLaunchViewModel!;

        public LaunchPage()
        {
            this.InitializeComponent();
            this.DataContext = ViewModel;
        }

        private void Intervals_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Only trigger if click, not drag
            ViewModel.ImportIntervalsFromFileCommand.Execute(null);
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
                        await ViewModel.ProcessIntervalFile(storageFile.Path);
                    }
                }
            }
        }

        private void CameraList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListView listView)
            {
                ViewModel.SelectedCameras.Clear();
                foreach (var item in listView.SelectedItems)
                {
                    if (item is string camName)
                    {
                        ViewModel.SelectedCameras.Add(camName);
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

        private void DropArea_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (DropAreaGrid != null)
                DropAreaGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 45, 48)); // Lighter dark
            if (DropAreaBorder != null)
                DropAreaBorder.Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 163, 224)); // Milestone Blue
        }

        private void DropArea_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (DropAreaGrid != null)
                DropAreaGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 37)); // Original #252525
            if (DropAreaBorder != null)
                DropAreaBorder.Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 68, 68, 68)); // Original #444
        }
    }
}
