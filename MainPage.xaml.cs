using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.Streams;
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
            LoadSettings();
            InitializeComponent();
        }

        private async void LoadSettings()
        {
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            var source = localSettings.Values["source"];
            if (source != null)
                try
                {
                    sourceFolder = await StorageFolder.GetFolderFromPathAsync(source.ToString());
                    textBlockSource.Text = sourceFolder.Path.ToString();
                }
                catch (Exception) {}
            var destination = localSettings.Values["destination"];
            if (destination != null)
                try
                {
                    destinationFolder = await StorageFolder.GetFolderFromPathAsync(destination.ToString());
                    textBlockDestination.Text = destinationFolder.Path.ToString();
                }
                catch (Exception){}

            progressBar.Value = 0;
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
                FutureAccessList.AddOrReplace("PickedFolderToken", folder);
                button.Content = "" + folder.Name;
                if (button.Tag.ToString() == "source")
                {
                    sourceFolder = folder;
                    SaveSettings();
                }
                else
                if (button.Tag.ToString() == "destination")
                {
                    destinationFolder = folder;
                    SaveSettings();
                }
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
            try
            {
                button.IsEnabled = false;
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

                    using (var stream = await sourceFile.OpenStreamForReadAsync())
                    {
                        XDocument doc = XDocument.Load(stream);
                        var namespaceManager = new XmlNamespaceManager(new NameTable());
                        namespaceManager.AddNamespace("fb", "http://www.gribuser.ru/xml/fictionbook/2.0");
                        XElement title = doc.Root.XPathSelectElement("//fb:title-info", namespaceManager);
                        if (title != null)
                        {
                            XElement author = title.XPathSelectElement("//fb:author", namespaceManager);

                            XElement lastNameElement = author.XPathSelectElement("fb:last-name", namespaceManager);
                            string lastName = lastNameElement != null ? lastNameElement.Value : "";
                            XElement middleNameElement = author.XPathSelectElement("fb:middle-name", namespaceManager);
                            string middleName = middleNameElement != null ? middleNameElement.Value : "";
                            XElement firstNameElement = author.XPathSelectElement("fb:first-name", namespaceManager);
                            string firstName = firstNameElement != null ? firstNameElement.Value : "";

                            XElement bookTitleElement = title.XPathSelectElement("fb:book-title", namespaceManager);
                            string bookTitle = bookTitleElement != null ? bookTitleElement.Value : "";
                            Log(log, $"{lastName} - {middleName} - {firstName} '{bookTitle}' = {fileName}");
                        }
                       
                    }
                    //CopyFile(files, sourceFile, fileName);
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

        private async void CopyFile(IReadOnlyList<StorageFile> files, StorageFile sourceFile, string fileName)
        {
            bool exists = await destinationFolder.FileExistsAsync(fileName);
            if (!exists)
            {
                StorageFile destinationFile = await destinationFolder.CreateFileAsync(sourceFile.Name);
                using (var sourceStream = (await sourceFile.OpenReadAsync()).GetInputStreamAt(0))
                {
                    using (var destinationStream = (await destinationFile.OpenAsync(FileAccessMode.ReadWrite)).GetOutputStreamAt(0))
                    {
                        await RandomAccessStream.CopyAndCloseAsync(sourceStream, destinationStream);
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
