using AutoDuty.Helpers;
using System.Security.Cryptography;
using ECommons;
using Serilog.Events;
using AutoDuty.Windows;

namespace AutoDuty.Updater
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using static Data.Classes;
    internal static class FileHelper
    {
        internal static readonly FileSystemWatcher FileSystemWatcher = new(Plugin.pathsDirectory.FullName)
        {
            NotifyFilter = NotifyFilters.Attributes
                                                                                        | NotifyFilters.CreationTime
                                                                                        | NotifyFilters.DirectoryName
                                                                                        | NotifyFilters.FileName
                                                                                        | NotifyFilters.LastAccess
                                                                                        | NotifyFilters.LastWrite
                                                                                        | NotifyFilters.Security
                                                                                        | NotifyFilters.Size,

            Filter = "*.json",
            IncludeSubdirectories = true
        };

        internal static readonly FileSystemWatcher fileWatcher = new();

        private static readonly Lock updateLock = new();


        public static byte[] CalculateMD5(string filename)
        {
            using MD5?        md5    = MD5.Create();
            using FileStream? stream = File.OpenRead(filename);
            return md5.ComputeHash(stream);
        }

        private static void LogInit()
        {
            string? path = $"{Plugin.dalamudDirectory}/dalamud.log";
            if (!File.Exists(path)) 
                return;

            FileInfo file = new(path);
            if (!file.Exists) 
                return;

            string? directory = file.DirectoryName;
            string? filename  = file.Name;
            if (directory.IsNullOrEmpty() || filename.IsNullOrEmpty())
                return;

            long lastMaxOffset = file.Length;

            fileWatcher.Path = directory!;
            fileWatcher.Filter = filename;
            fileWatcher.NotifyFilter = NotifyFilters.LastWrite;

            fileWatcher.Changed += (_, _) =>
            {
                using FileStream fs = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(lastMaxOffset, SeekOrigin.Begin);
                using StreamReader sr = new(fs);

                while (sr.ReadLine() is { } x)
                {
                    if (!x.Contains("[AutoDuty]")) continue;

                    LogMessage logEntry = new() { Message = x };

                    if (x.Contains("[FTL]"))
                        logEntry.LogEventLevel = LogEventLevel.Fatal;
                    else if (x.Contains("[ERR]"))
                        logEntry.LogEventLevel = LogEventLevel.Error;
                    else if (x.Contains("[WRN]"))
                        logEntry.LogEventLevel = LogEventLevel.Warning;
                    else if (x.Contains("[INF]"))
                        logEntry.LogEventLevel = LogEventLevel.Information;
                    else if (x.Contains("[DBG]"))
                        logEntry.LogEventLevel = LogEventLevel.Debug;
                    else if (x.Contains("[VRB]"))
                        logEntry.LogEventLevel = LogEventLevel.Verbose;
                    LogTab.Add(logEntry);
                }
                lastMaxOffset = fs.Position;
            };
            fileWatcher.EnableRaisingEvents = true;
        }

        internal static void Init()
        {
            FileSystemWatcher.Changed += OnChanged;
            FileSystemWatcher.Created += OnCreated;
            FileSystemWatcher.Deleted += OnDeleted;
            FileSystemWatcher.Renamed += OnRenamed;
            FileSystemWatcher.EnableRaisingEvents = true;
            Update();
            LogInit();
        }

        private static void Update()
        {
            lock (updateLock)
            {
                ContentPathsManager.DictionaryPaths = [];

                MainTab.PathsUpdated();
                PathsTab.PathsUpdated();

                foreach ((_, Content content) in ContentHelper.DictionaryContent)
                {
                    IEnumerable<FileInfo> files = Plugin.pathsDirectory.EnumerateFiles($"({content.TerritoryType})*.json", SearchOption.AllDirectories);

                    foreach (FileInfo file in files)
                    {
                        if (!ContentPathsManager.DictionaryPaths.ContainsKey(content.TerritoryType))
                            ContentPathsManager.DictionaryPaths.Add(content.TerritoryType, new ContentPathsManager.ContentPathContainer(content));

                        ContentPathsManager.DictionaryPaths[content.TerritoryType].AddPath(file.FullName);
                    }
                }
            }
        }

        private static void OnChanged(object sender, FileSystemEventArgs e) => Update();

        private static void OnCreated(object sender, FileSystemEventArgs e) => Update();

        private static void OnDeleted(object sender, FileSystemEventArgs e) => Update();

        private static void OnRenamed(object sender, RenamedEventArgs e) => Update();
    }
}
