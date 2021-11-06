using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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

namespace AppRuntimeComponent
{
    public sealed class AppBackgroundTask : IBackgroundTask
    {
        private StorageFolder sourceFolder;
        private StorageFolder destinationFolder;
        BackgroundTaskDeferral _deferral;

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            _deferral = taskInstance.GetDeferral();
            //
            // TODO: Insert code to start one or more asynchronous methods using the
            //       await keyword, for example:
            //
            // await ExampleMethodAsync();
            //
            Thread.Sleep(1000);

            _deferral.Complete();

        }


        private async Task Migrate()
        {
            if (sourceFolder == null || destinationFolder == null)
            {
                throw new Exception("Select folder");
            }

            QueryOptions queryOption = new QueryOptions
                (CommonFileQuery.OrderByTitle, new string[] { ".fb2", ".zip" });

            queryOption.FolderDepth = FolderDepth.Deep;

            CommonFileQuery query = CommonFileQuery.OrderByName;
            var files = await sourceFolder.GetFilesAsync(query);

            //count = 0;
            var tasks = new List<Task>();

            StorageFile logFile = await destinationFolder.CreateFileAsync("log.txt", CreationCollisionOption.ReplaceExisting);
            var handle = logFile.CreateSafeFileHandle(options: FileOptions.RandomAccess);
            using (FileStream log = new FileStream(handle, FileAccess.ReadWrite))
            //using (StreamWriter log = new StreamWriter(logFile.Path, false, System.Text.Encoding.UTF8))
            //using (var log = (await logFile.OpenAsync(FileAccessMode.ReadWrite)).GetOutputStreamAt(0))
            {
                foreach (StorageFile sourceFile in files)
                {
                    string fileName = sourceFile.Name;
                    if (fileName.EndsWith(".fb2") || sourceFile.Name.EndsWith(".zip"))
                    {
                        Task task = Copy(sourceFile);
                        //count++;
                        //progressBar.Value = 100 * count / amount;
                        //tasks.Add(task);
                    }
                }
            }
            //await Task.WhenAll(tasks);
            //TextBlock1.Text = $"Count: {count}";
        }

        private async Task Copy(StorageFile sourceFile)
        {
            string path;
            using (Stream sourceStream = await sourceFile.OpenStreamForReadAsync())
            {
                using (Stream stream = (sourceFile.Name.EndsWith(".zip") ? new GZipStream(sourceStream, CompressionMode.Decompress) : sourceStream))
                {
                    path = createFileName(stream, sourceFile.Name);
                }
            }
            FileInfo file = new FileInfo(Path.Combine(destinationFolder.Path, path));
            if (!file.Exists)
            {
                if (!file.Directory.Exists)
                {
                    file.Directory.Create();
                }
                CopyFile(sourceFile, file);
            }
        }

        private string createFileName(Stream stream, string fileName)
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
                lastName = regex.Replace(lastName, "");
                firstName = regex.Replace(firstName, "");

                string folderName = $"{lastName}_{firstName}";
                fileName = $"{lastName}_{firstName}-{regex.Replace(bookTitle, "_")}.{Path.GetExtension(fileName)}";

                return Path.Combine(folderName, fileName);
            }
            else
            {
                return fileName;
            }
        }

        private void Log(FileStream log, string line)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            log.Write(bytes, 0, bytes.Length);
            //log.Flush();
        }

        private async void CopyFile(StorageFile sourceFile, FileInfo file)
        {
            StorageFile destinationFile = await destinationFolder.CreateFileAsync(file.Name);
            using (var sourceStream = (await sourceFile.OpenReadAsync()).GetInputStreamAt(0))
            {
                using (var destinationStream = (await destinationFile.OpenAsync(FileAccessMode.ReadWrite)).GetOutputStreamAt(0))
                {
                    await RandomAccessStream.CopyAndCloseAsync(sourceStream, destinationStream);
                }
            }
        }
    }
}