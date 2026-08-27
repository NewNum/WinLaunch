using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace WinLaunch
{
    public class EventArgsFilesAdded : EventArgs
    {
        public List<string> Files { get; set; }
    }

    /// <summary>
    /// Watches directories for newly appearing shortcuts and reports them in debounced batches.
    /// Installers tend to drop several files at once, hence the batching.
    /// </summary>
    internal class ShortcutFolderWatcher : IDisposable
    {
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly DispatcherTimer debounce = new DispatcherTimer();
        private readonly Dispatcher dispatcher;
        private readonly object pendingLock = new object();

        private List<string> pending = new List<string>();
        private bool disposed;

        public event EventHandler<EventArgsFilesAdded> FilesAdded;

        public ShortcutFolderWatcher(IEnumerable<string> directories, bool includeSubdirectories, TimeSpan debounceInterval)
        {
            dispatcher = Dispatcher.CurrentDispatcher;

            debounce.Interval = debounceInterval;
            debounce.Tick += Debounce_Tick;

            foreach (string directory in Distinct(directories))
            {
                FileSystemWatcher watcher = TryCreateWatcher(directory, includeSubdirectories);

                if (watcher != null)
                    watchers.Add(watcher);
            }
        }

        private static IEnumerable<string> Distinct(IEnumerable<string> directories)
        {
            return directories
                .Where(directory => !string.IsNullOrEmpty(directory))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private FileSystemWatcher TryCreateWatcher(string directory, bool includeSubdirectories)
        {
            if (!Directory.Exists(directory))
                return null;

            try
            {
                FileSystemWatcher watcher = new FileSystemWatcher(directory);

                watcher.IncludeSubdirectories = includeSubdirectories;
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;

                //Created alone is unreliable for shortcuts, which are often created empty
                //and filled in afterwards, so LastWrite and Renamed are needed as well
                watcher.Created += OnChanged;
                watcher.Changed += OnChanged;
                watcher.Renamed += OnChanged;

                watcher.EnableRaisingEvents = true;

                return watcher;
            }
            catch
            {
                //a directory we are not allowed to watch simply goes unwatched
                return null;
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (!InstalledAppScanner.IsShortcut(e.FullPath))
                return;

            lock (pendingLock)
            {
                pending.Add(e.FullPath);
            }

            //file system events arrive on thread pool threads, the timer belongs to the UI thread
            dispatcher.BeginInvoke(new Action(RestartDebounce));
        }

        private void RestartDebounce()
        {
            if (disposed)
                return;

            debounce.Stop();
            debounce.Start();
        }

        private void Debounce_Tick(object sender, EventArgs e)
        {
            debounce.Stop();

            List<string> batch;

            lock (pendingLock)
            {
                batch = pending;
                pending = new List<string>();
            }

            //an installer may have replaced or removed a file again before the batch fired
            batch = batch
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(File.Exists)
                .ToList();

            if (batch.Count == 0)
                return;

            EventHandler<EventArgsFilesAdded> handler = FilesAdded;

            if (handler != null)
                handler(this, new EventArgsFilesAdded { Files = batch });
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            foreach (FileSystemWatcher watcher in watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Created -= OnChanged;
                    watcher.Changed -= OnChanged;
                    watcher.Renamed -= OnChanged;
                    watcher.Dispose();
                }
                catch { }
            }

            watchers.Clear();

            debounce.Stop();
            debounce.Tick -= Debounce_Tick;
        }
    }
}
