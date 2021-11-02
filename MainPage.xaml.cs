using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

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
                catch (Exception) {}
            var destination = localSettings.Values["destination"];
            if (destination != null)
                try
                {
                    StorageFolder.GetFolderFromPathAsync(destination.ToString()).Completed = SetDestinationFolder;
                }
                catch (Exception){}
        }

        private void SetSourceFolder(IAsyncOperation<StorageFolder> asyncInfo, AsyncStatus asyncStatus)
        {
            if (asyncStatus.Equals(AsyncStatus.Completed))
            {
                sourceFolder = asyncInfo.GetResults();
                Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    textBlockSource.Text = sourceFolder.Path.ToString();
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
                    textBlockDestination.Text = destinationFolder.Path.ToString();
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
            textBlockSource.Text = sourceFolder.Path.ToString();
            localSettings.Values["destination"] = destinationFolder.Path.ToString();
            textBlockDestination.Text = destinationFolder.Path.ToString();
        }

        private async void RunMigration_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            RunMigration_Click(button);
        }

        private async void RunMigration_Click(Button button)
        {
            button.IsEnabled = false;
            progressBar.Value = 0;
            try
            {
                Migrate();
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
                progressBar.Value = 100;
            }
        }

        private async void Migrate()
        {
            if (sourceFolder == null || destinationFolder == null)
            {
                throw new Exception("Select folder");
            }

            QueryOptions queryOption = new QueryOptions
                (CommonFileQuery.OrderByTitle, new string[] { ".fb2", ".zip" });

            queryOption.FolderDepth = FolderDepth.Deep;

            CommonFileQuery query = CommonFileQuery.OrderByName;
            var files = await sourceFolder
            .GetFilesAsync(query);
            //.CreateFileQueryWithOptions(queryOption)
            //.GetFilesAsync();

            count = 0;

            StorageFile logFile = await destinationFolder.CreateFileAsync("log.txt", CreationCollisionOption.ReplaceExisting);
            var handle = logFile.CreateSafeFileHandle(options: FileOptions.RandomAccess);
            using (FileStream log = new FileStream(handle, FileAccess.ReadWrite))
            //using (StreamWriter log = new StreamWriter(logFile.Path, false, System.Text.Encoding.UTF8))
            //using (var log = (await logFile.OpenAsync(FileAccessMode.ReadWrite)).GetOutputStreamAt(0))
            {
                foreach (var sourceFile in files)
                {
                    string fileName = sourceFile.Name;
                    if (!(fileName.EndsWith(".fb2") /*|| sourceFile.Name.EndsWith(".zip")*/))
                    {
                        continue;
                    }

                    using (Stream stream = await sourceFile.OpenStreamForReadAsync())
                    {
                        XDocument doc = XDocument.Load(stream);
                        var namespaceManager = new XmlNamespaceManager(new NameTable());
                        namespaceManager.AddNamespace("fb", "http://www.gribuser.ru/xml/fictionbook/2.0");
                        XElement title = doc.Root.XPathSelectElement("//fb:title-info", namespaceManager);
                        if (title != null)
                        {
                            XElement author = title.XPathSelectElement("//fb:author", namespaceManager);

                            XElement lastNameElement = author.XPathSelectElement("fb:last-name", namespaceManager);
                            string lastName = lastNameElement != null ? lastNameElement.Value : "None";
                            XElement middleNameElement = author.XPathSelectElement("fb:middle-name", namespaceManager);
                            string middleName = middleNameElement != null ? middleNameElement.Value : "";
                            XElement firstNameElement = author.XPathSelectElement("fb:first-name", namespaceManager);
                            string firstName = firstNameElement != null ? firstNameElement.Value : "None";

                            XElement bookTitleElement = title.XPathSelectElement("fb:book-title", namespaceManager);
                            string bookTitle = bookTitleElement != null ? bookTitleElement.Value : "";

                            Regex regex = new Regex("\\W");

                            string folderName = $"{regex.Replace(lastName, "")}_{regex.Replace(firstName, "")}";
                            StorageFolder folder = await destinationFolder.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);

                            fileName = $"{regex.Replace(lastName, "")}_{regex.Replace(firstName, "")}-{regex.Replace(bookTitle, "_")}";
                            fileName += sourceFile.Name.Substring(sourceFile.Name.LastIndexOf("."));

                            CopyFile(files, sourceFile, fileName, folder);
                            Log(log, $"{lastName} - {middleName} - {firstName} '{bookTitle}' = {fileName} = {sourceFile.Name}");
                        }
                        else
                        {
                            Log(log, $"{fileName} - cannot parse");
                        }

                    }
                }

                textBlock1.Text = $"Count: {count}";
            }
        }

        private void Log(FileStream log, string line)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            log.Write(bytes, 0, bytes.Length);
            //log.Flush();
        }

        private async void CopyFile(IReadOnlyList<StorageFile> files, StorageFile sourceFile, string fileName, StorageFolder destinationFolder)
        {
            bool exists = destinationFolder.FileExistsAsync(fileName).Result;
            if (!exists)
            {
                StorageFile destinationFile = await destinationFolder.CreateFileAsync(fileName);
                using (var sourceStream = (await sourceFile.OpenReadAsync()).GetInputStreamAt(0))
                {
                    using (var destinationStream = (await destinationFile.OpenAsync(FileAccessMode.ReadWrite)).GetOutputStreamAt(0))
                    {
                        ulong result = await RandomAccessStream.CopyAndCloseAsync(sourceStream, destinationStream);
                    }
                }

                count++;
                progressBar.Value = 100 * count / files.Count;
            }
        }

        private void progressBar_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            textBlock1.Text = $"{progressBar.Value} %";
        }
    }
}
