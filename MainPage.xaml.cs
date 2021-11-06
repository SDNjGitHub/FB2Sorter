using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Windows.ApplicationModel.Background;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace FB2Sorter
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private StorageFolder sourceFolder;
        private StorageFolder destinationFolder;
        private int count = 0;
        private string taskName = "Migration";

        public MainPage()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            var source = localSettings.Values["source"];
            if (source != null)
                try
                {
                    StorageFolder.GetFolderFromPathAsync(source.ToString()).Completed = SetSourceFolder;
                }
                catch (Exception) { }
            var destination = localSettings.Values["destination"];
            if (destination != null)
                try
                {
                    StorageFolder.GetFolderFromPathAsync(destination.ToString()).Completed = SetDestinationFolder;
                }
                catch (Exception) { }
        }

        private void SetSourceFolder(IAsyncOperation<StorageFolder> asyncInfo, AsyncStatus asyncStatus)
        {
            if (asyncStatus.Equals(AsyncStatus.Completed))
            {
                sourceFolder = asyncInfo.GetResults();
                Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    TextBlockSource.Text = sourceFolder.Path.ToString();
                });
            }
        }

        private void SetDestinationFolder(IAsyncOperation<StorageFolder> asyncInfo, AsyncStatus asyncStatus)
        {
            if (asyncStatus.Equals(AsyncStatus.Completed))
            {
                destinationFolder = asyncInfo.GetResults();
                Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    TextBlockDestination.Text = destinationFolder.Path.ToString();
                });
            }
        }

        private async void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");

            StorageFolder folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                // Application now has read/write access to all contents in the picked folder
                // (including other sub-folder contents)
                Windows.Storage.AccessCache.StorageApplicationPermissions.
                FutureAccessList.AddOrReplace("PickedFolderToken" + button.Tag.ToString(), folder);
                button.Content = "" + folder.Name;
                if (button.Tag.ToString() == "source")
                {
                    sourceFolder = folder;
                }
                else
                if (button.Tag.ToString() == "destination")
                {
                    destinationFolder = folder;
                }
                SaveSettings();
            }
            else
            {
                button.Content = button.DataContext;
            }
        }

        private void SaveSettings()
        {
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["source"] = sourceFolder.Path.ToString();
            TextBlockSource.Text = sourceFolder.Path.ToString();
            localSettings.Values["destination"] = destinationFolder.Path.ToString();
            TextBlockDestination.Text = destinationFolder.Path.ToString();
        }

        private async void RunMigration_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            RunMigration_Click(button);
        }

        private async void StopMigration_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
            () =>
            {
                var taskList = BackgroundTaskRegistration.AllTasks.Values;
                var task = taskList.FirstOrDefault(i => i.Name == taskName);
                if (task == null)
                {
                    task.Unregister(true);
                }

                RunMigration.IsEnabled = true;
                StopMigration.IsEnabled = false;
            });
        }


        private async void RunMigration_Click(Button button)
        {
            button.IsEnabled = false;
            ProgressBar.Value = 0;
            try
            {
                await Migrate();
            }
            catch (Exception exc)
            {
                ContentDialog errorDialog = new ContentDialog()
                {
                    Title = "Error",
                    Content = $"Error: {exc}",
                    CloseButtonText = "Ok"
                };

                _ = await errorDialog.ShowAsync();
            }
            finally
            {
                button.IsEnabled = true;
                ProgressBar.Value = 100;
            }
        }

        private async Task Migrate()
        {
            var taskList = BackgroundTaskRegistration.AllTasks.Values;
            var task = taskList.FirstOrDefault(i => i.Name == taskName);
            if (task == null)
            {
                var taskBuilder = new BackgroundTaskBuilder();
                taskBuilder.Name = taskName;
                taskBuilder.TaskEntryPoint = typeof(AppRuntimeComponent.AppBackgroundTask).ToString();

                ApplicationTrigger appTrigger = new ApplicationTrigger();
                taskBuilder.SetTrigger(appTrigger);

                task = taskBuilder.Register();

                task.Progress += Task_Progress;
                task.Completed += Task_Completed;

                await appTrigger.RequestAsync();

                RunMigration.IsEnabled = false;
                StopMigration.IsEnabled = true;
            }
        }

        private void Task_Completed(BackgroundTaskRegistration sender, BackgroundTaskCompletedEventArgs args)
        {
            var result = ApplicationData.Current.LocalSettings.Values["factorial"];
            var progress = $"Результат: {result}";
            UpdateUI(progress);
            Stop();
        }

        private void Stop()
        {
        }

        private void Task_Progress(BackgroundTaskRegistration sender, BackgroundTaskProgressEventArgs args)
        {
            ProgressBar.Value = args.Progress;
            //var progress = $"Progress: {args.Progress} %";
            // UpdateUI(progress);
        }

        private async void UpdateUI(string progress)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
            () =>
            {
                TextBlock1.Text = progress;
            });
        }

        private void ProgressBar_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            TextBlock1.Text = $"{ProgressBar.Value} %";
        }
    }
}
