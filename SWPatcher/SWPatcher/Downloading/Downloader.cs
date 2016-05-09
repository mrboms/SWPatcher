﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using SWPatcher.General;
using SWPatcher.Helpers.GlobalVar;

namespace SWPatcher.Downloading
{
    public class Downloader
    {
        private readonly BackgroundWorker Worker;
        private readonly WebClient Client;
        private readonly List<SWFile> SWFiles;
        private Language Language;
        private int DownloadIndex;

        public Downloader(List<SWFile> swFiles)
        {
            this.SWFiles = swFiles;
            this.Worker = new BackgroundWorker
            {
                WorkerSupportsCancellation = true
            };
            this.Worker.DoWork += new DoWorkEventHandler(Worker_DoWork);
            this.Worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(Worker_RunWorkerCompleted);
            this.Client = new WebClient();
            this.Client.DownloadProgressChanged += new DownloadProgressChangedEventHandler(Client_DownloadProgressChanged);
            this.Client.DownloadFileCompleted += new AsyncCompletedEventHandler(Client_DownloadFileCompleted);
        }

        public event DownloaderProgressChangedEventHandler DownloaderProgressChanged;
        public event DownloaderCompletedEventHandler DownloaderCompleted;

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            if (HasNewDownload() || Convert.ToBoolean(e.Argument))
            {
                this.SWFiles.Clear();
                using (var client = new WebClient())
                using (var file = new TempFile())
                {
                    client.DownloadFile(Uris.PatcherGitHubHome + Strings.IniName.TranslationPackData, file.Path);
                    IniReader dataIni = new IniReader(file.Path);
                    var array = dataIni.GetSectionNames();
                    foreach (var fileName in array)
                    {
                        string name = fileName as string;
                        dataIni.Section = name;
                        string path = dataIni.ReadString(Strings.IniName.Pack.KeyPath);
                        string pathA = dataIni.ReadString(Strings.IniName.Pack.KeyPathInArchive);
                        string pathD = dataIni.ReadString(Strings.IniName.Pack.KeyPathOfDownload);
                        string format = dataIni.ReadString(Strings.IniName.Pack.KeyFormat);
                        this.SWFiles.Add(new SWFile(name, path, pathA, pathD, format));
                        if (Worker.CancellationPending)
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                }
            }
            else
                throw new Exception(string.Format("You already have the latest({0} JST) translation files for this language!", Strings.DateToString(this.Language.LastUpdate)));
        }

        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled || e.Error != null)
                this.OnDownloaderComplete(sender, new DownloaderCompletedEventArgs(e.Cancelled, e.Error));
            else
            {
                this.DownloadIndex = 0;
                this.DownloadNext();
            }
        }

        private void Client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            this.OnDownloaderProgressChanged(sender, new DownloaderProgressChangedEventArgs(DownloadIndex + 1, SWFiles.Count, Path.GetFileNameWithoutExtension(SWFiles[DownloadIndex].Name), e));
        }

        private void Client_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Cancelled || e.Error != null)
                this.OnDownloaderComplete(sender, new DownloaderCompletedEventArgs(e.Cancelled, e.Error));
            else
            {
                if (SWFiles.Count > ++this.DownloadIndex)
                    DownloadNext();
                else
                    this.OnDownloaderComplete(sender, new DownloaderCompletedEventArgs(this.Language, e.Cancelled, e.Error));
            }
        }

        private void OnDownloaderProgressChanged(object sender, DownloaderProgressChangedEventArgs e)
        {
            if (this.DownloaderProgressChanged != null)
                this.DownloaderProgressChanged(sender, e);
        }

        private void OnDownloaderComplete(object sender, DownloaderCompletedEventArgs e)
        {
            if (this.DownloaderCompleted != null)
                this.DownloaderCompleted(sender, e);
        }

        private void DownloadNext()
        {
            Uri uri = new Uri(Uris.TranslationGitHubHome + this.Language.Lang + '/' + SWFiles[DownloadIndex].PathD);
            string path = "";
            if (string.IsNullOrEmpty(SWFiles[DownloadIndex].Path))
                path = Path.Combine(Paths.PatcherRoot, this.Language.Lang);
            else
                path = Path.Combine(Path.GetDirectoryName(Path.Combine(Paths.PatcherRoot, this.Language.Lang, SWFiles[DownloadIndex].Path)), Path.GetFileNameWithoutExtension(SWFiles[DownloadIndex].Path));
            DirectoryInfo directoryDestination = new DirectoryInfo(path);
            if (!directoryDestination.Exists)
                directoryDestination.Create();
            string fileDestination = Path.Combine(directoryDestination.FullName, Path.GetFileName(SWFiles[DownloadIndex].PathD));
            this.Client.DownloadFileAsync(uri, fileDestination);
        }

        private bool HasNewDownload()
        {
            DirectoryInfo directory = new DirectoryInfo(Path.Combine(Paths.PatcherRoot, this.Language.Lang));
            if (directory.Exists)
            {
                string filePath = Path.Combine(directory.FullName, Strings.IniName.Translation);
                if (File.Exists(filePath))
                {
                    IniReader translationIni = new IniReader(filePath);
                    string date = translationIni.ReadString(Strings.IniName.Patcher.Section, Strings.IniName.Pack.KeyDate, Strings.DateToString(DateTime.MinValue));
                    if (this.Language.LastUpdate > Strings.ParseExact(date))
                        return true;
                }
                else
                    return true;
            }
            else
            {
                directory.Create();
                return true;
            }
            return false;
        }

        public void Cancel()
        {
            this.Worker.CancelAsync();
            this.Client.CancelAsync();
        }

        public void Run(Language language, bool isForced)
        {
            if (this.Worker.IsBusy || this.Client.IsBusy)
                return;
            this.Language = language;
            this.Worker.RunWorkerAsync(isForced);
        }
    }
}
