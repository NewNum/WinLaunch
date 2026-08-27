using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WinLaunch
{
    partial class MainWindow : Window
    {
        ShortcutFolderWatcher desktopWatcher;
        ShortcutFolderWatcher startMenuWatcher;

        private bool appScanRunning = false;

        #region Watchers

        private void StartDesktopWatcher()
        {
            StopDesktopWatcher();

            desktopWatcher = new ShortcutFolderWatcher(
                new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
                },
                false,
                TimeSpan.FromMilliseconds(400));

            desktopWatcher.FilesAdded += desktopWatcher_FilesAdded;
        }

        private void StopDesktopWatcher()
        {
            if (desktopWatcher == null)
                return;

            desktopWatcher.FilesAdded -= desktopWatcher_FilesAdded;
            desktopWatcher.Dispose();
            desktopWatcher = null;
        }

        private void StartStartMenuWatcher()
        {
            StopStartMenuWatcher();

            //an installer writes a whole program group at once, so give it time to settle
            startMenuWatcher = new ShortcutFolderWatcher(
                InstalledAppScanner.GetStartMenuRoots(),
                true,
                TimeSpan.FromSeconds(3));

            startMenuWatcher.FilesAdded += startMenuWatcher_FilesAdded;
        }

        private void StopStartMenuWatcher()
        {
            if (startMenuWatcher == null)
                return;

            startMenuWatcher.FilesAdded -= startMenuWatcher_FilesAdded;
            startMenuWatcher.Dispose();
            startMenuWatcher = null;
        }

        private void desktopWatcher_FilesAdded(object sender, EventArgsFilesAdded e)
        {
            AddNewShortcuts(e.Files, Settings.CurrentSettings.DeleteDesktopLinksAfterAdding);
        }

        private void startMenuWatcher_FilesAdded(object sender, EventArgsFilesAdded e)
        {
            //never delete anything out of the start menu
            AddNewShortcuts(e.Files, false);
        }

        /// <summary>
        /// Adds the shortcuts that do not resolve to an application already on the springboard.
        /// </summary>
        private void AddNewShortcuts(List<string> files, bool deleteSourceFiles)
        {
            SBM.CloseFolderInstant();
            SBM.EndSearch();

            HashSet<string> identities = CollectItemIdentities();
            bool added = false;

            foreach (string file in files)
            {
                string identity = AppIdentity.ForFile(file);

                if (identity == null || !identities.Add(identity))
                    continue;

                AddFile(file);
                added = true;

                if (deleteSourceFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }
            }

            if (added)
                TriggerSaveItemsDelayed();
        }

        #endregion Watchers

        #region Identity

        private IEnumerable<SBItem> EnumerateAllItems()
        {
            foreach (SBItem item in SBM.IC.Items.ToList())
            {
                yield return item;

                if (item.IsFolder)
                {
                    foreach (SBItem subItem in item.IC.Items.ToList())
                        yield return subItem;
                }
            }
        }

        private List<AppIdentitySnapshot> SnapshotAllItems()
        {
            return EnumerateAllItems().Select(AppIdentitySnapshot.From).ToList();
        }

        private HashSet<string> CollectItemIdentities()
        {
            HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);

            foreach (AppIdentitySnapshot snapshot in SnapshotAllItems())
            {
                string identity = AppIdentity.ForItem(snapshot);

                if (identity != null)
                    identities.Add(identity);
            }

            return identities;
        }

        #endregion Identity

        #region Refresh installed apps

        private void AddDefaultApps()
        {
            RefreshInstalledApps(false);
        }

        /// <summary>
        /// Rescans the start menu and folds the result into the springboard. The scan itself runs
        /// on a background thread because resolving thousands of shortcuts is a slow COM operation.
        /// </summary>
        public void RefreshInstalledApps(bool reportResult)
        {
            if (appScanRunning)
                return;

            appScanRunning = true;

            SBM.CloseFolderInstant();
            SBM.EndSearch();

            List<AppIdentitySnapshot> snapshots = SnapshotAllItems();

            bool removeUninstalled = Settings.CurrentSettings.RemoveUninstalledApps;

            Thread scanThread = new Thread(() =>
            {
                InstalledAppScanResult scan = null;
                List<SBItem> uninstalled = new List<SBItem>();

                try
                {
                    //a reinstall must not be judged by what a previous scan resolved
                    AppIdentity.ClearCache();

                    HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);

                    foreach (AppIdentitySnapshot snapshot in snapshots)
                    {
                        if (removeUninstalled && AppIdentity.IsTargetMissing(snapshot))
                        {
                            //this entry is about to go, so it must not suppress a working replacement
                            uninstalled.Add(snapshot.Item);
                            continue;
                        }

                        string identity = AppIdentity.ForItem(snapshot);

                        if (identity != null)
                            identities.Add(identity);
                    }

                    scan = InstalledAppScanner.Scan(identities);
                }
                catch (Exception ex)
                {
                    CrashReporter.Report(ex);
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    int added = 0;
                    int removed = 0;

                    try
                    {
                        if (uninstalled != null)
                            removed = RemoveItems(uninstalled);

                        if (scan != null)
                            added = ApplyScanResult(scan);
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Report(ex);
                    }
                    finally
                    {
                        appScanRunning = false;
                    }

                    if (reportResult)
                        ReportRefreshResult(added, removed);
                }));
            });

            scanThread.IsBackground = true;
            scanThread.Name = "WinLaunch app scan";

            //the shell COM objects used to resolve shortcuts expect an STA thread
            scanThread.SetApartmentState(ApartmentState.STA);
            scanThread.Start();
        }

        private void ReportRefreshResult(int added, int removed)
        {
            string message = string.Format(
                TranslationSource.Instance["RefreshInstalledAppsResult"],
                added,
                removed);

            MessageBox.Show(message, TranslationSource.Instance["RefreshInstalledApps"]);
        }

        private int ApplyScanResult(InstalledAppScanResult scan)
        {
            int added = 0;

            foreach (string file in scan.LooseFiles)
            {
                AddFile(file);
                added++;
            }

            foreach (ScannedAppFolder scannedFolder in scan.Folders)
            {
                SBItem folder = new SBItem(scannedFolder.Name, "", "", "Folder", null, "", SBItem.FolderIcon);
                folder.IsFolder = true;

                int gridIndex = 0;

                foreach (string file in scannedFolder.Files)
                {
                    SBItem item = PrepareFile(file);

                    if (item == null)
                        continue;

                    item.Page = 0;
                    item.GridIndex = gridIndex;

                    folder.IC.Items.Add(item);

                    gridIndex++;
                    added++;
                }

                if (folder.IC.Items.Count == 0)
                    continue;

                SBM.AddItem(folder);
                folder.UpdateFolderIcon(true);
            }

            if (added == 0)
                return 0;

            TriggerSaveItemsDelayed();

            if (Settings.CurrentSettings.SortItemsAlphabetically || Settings.CurrentSettings.SortFolderContentsOnly)
            {
                SortItemsAlphabetically();
            }

            return added;
        }

        #endregion Refresh installed apps

        #region Duplicate removal

        /// <summary>
        /// Drops entries that resolve to an application another entry already covers,
        /// keeping whichever one comes first in the current layout.
        /// </summary>
        public int RemoveDuplicateItems()
        {
            SBM.CloseFolderInstant();
            SBM.EndSearch();

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<SBItem> duplicates = new List<SBItem>();

            foreach (AppIdentitySnapshot snapshot in SnapshotAllItems())
            {
                string identity = AppIdentity.ForItem(snapshot);

                if (identity == null)
                    continue;

                if (!seen.Add(identity))
                    duplicates.Add(snapshot.Item);
            }

            return RemoveItems(duplicates);
        }

        /// <summary>
        /// Bulk removal that also works for entries inside closed folders, which the
        /// interactive remove path cannot handle because it operates on the open folder.
        /// </summary>
        private int RemoveItems(ICollection<SBItem> itemsToRemove)
        {
            if (itemsToRemove == null || itemsToRemove.Count == 0)
                return 0;

            SBM.CloseFolderInstant();

            HashSet<SBItem> removeSet = new HashSet<SBItem>(itemsToRemove);
            List<SBItem> touchedFolders = new List<SBItem>();

            //folder contents first, a folder may end up empty as a result
            foreach (SBItem folder in SBM.IC.Items.Where(item => item.IsFolder).ToList())
            {
                List<SBItem> children = folder.IC.Items.Where(removeSet.Contains).ToList();

                if (children.Count == 0)
                    continue;

                foreach (SBItem child in children)
                {
                    folder.IC.Items.Remove(child);

                    if (SBM.container.Contains(child.ContentRef))
                        SBM.container.Remove(child.ContentRef);
                }

                touchedFolders.Add(folder);
            }

            foreach (SBItem item in SBM.IC.Items.Where(removeSet.Contains).ToList())
                SBM.RemoveItemFromSB(item);

            foreach (SBItem folder in touchedFolders)
            {
                if (folder.IC.Items.Count == 0)
                {
                    SBM.RemoveItemFromSB(folder);
                    continue;
                }

                int gridIndex = 0;

                foreach (SBItem child in folder.IC.Items)
                {
                    child.GridIndex = gridIndex;
                    child.Page = 0;

                    gridIndex++;
                }

                folder.UpdateFolderIcon(true);
            }

            SBM.SP.TotalPages = SBM.GM.GetUsedPages();

            TriggerSaveItemsDelayed();

            return itemsToRemove.Count;
        }

        #endregion Duplicate removal

        public void SortItemsAlphabetically()
        {
            var items = new List<SBItem>();
            var folders = new List<SBItem>();

            foreach (var item in SBM.IC.Items)
            {
                if (item.IsFolder)
                {
                    folders.Add(item);
                }
                else
                {
                    items.Add(item);
                }
            }

            if (Settings.CurrentSettings.SortFolderContentsOnly)
            {
                //sort all items in folders
                foreach (var folder in folders)
                {
                    folder.IC.Items.Sort((a, b) =>
                        a.Name.CompareTo(b.Name)
                    );

                    //adjust grid indexes for items
                    int FolderGridIndex = 0;

                    foreach (var item in folder.IC.Items)
                    {
                        item.GridIndex = FolderGridIndex;
                        item.Page = 0;

                        FolderGridIndex++;
                    }

                    //update the folder icons, but hide the text for the active folder
                    folder.UpdateFolderIcon(folder != SBM.ActiveFolder);
                }

                return;
            }


            //sort all items alphabetically
            items.Sort((a, b) =>
                a.Name.CompareTo(b.Name)
            );

            folders.Sort((a, b) =>
                a.Name.CompareTo(b.Name)
            );

            //sort all items in folders
            foreach (var folder in folders)
            {
                folder.IC.Items.Sort((a, b) =>
                    a.Name.CompareTo(b.Name)
                );
            }

            //adjust grid indexes for items
            int ItemsPerPage = SBM.GM.XItems * SBM.GM.YItems;
            int GridIndex = 0;
            int Page = 0;

            if (Settings.CurrentSettings.SortFoldersFirst)
            {
                InsertFolders(folders, ItemsPerPage, ref GridIndex, ref Page);
            }

            foreach (var item in items)
            {
                item.GridIndex = GridIndex;
                item.Page = Page;

                GridIndex++;

                if (GridIndex == ItemsPerPage)
                {
                    GridIndex = 0;
                    Page++;
                }
            }

            if (!Settings.CurrentSettings.SortFoldersFirst)
            {
                InsertFolders(folders, ItemsPerPage, ref GridIndex, ref Page);
            }

            //update page count
            SBM.SP.TotalPages = SBM.GM.GetUsedPages();

            TriggerSaveItemsDelayed();
        }

        private void InsertFolders(List<SBItem> folders, int ItemsPerPage, ref int GridIndex, ref int Page)
        {
            //adjust grid indexes for folders
            foreach (var folder in folders)
            {
                folder.GridIndex = GridIndex;
                folder.Page = Page;

                //position the items in the folder
                int subGridIndex = 0;

                foreach (var subItem in folder.IC.Items)
                {
                    subItem.GridIndex = subGridIndex;
                    subItem.Page = 0;
                    subGridIndex++;
                }

                //update the folder icons, but hide the text for the active folder
                folder.UpdateFolderIcon(folder != SBM.ActiveFolder);

                GridIndex++;

                if (GridIndex == ItemsPerPage)
                {
                    GridIndex = 0;
                    Page++;
                }
            }
        }

        public void ClearAllItems()
        {
            SBM.CloseFolderInstant();

            //clear all items 
            foreach (var item in SBM.IC.Items)
            {
                SBM.container.Remove(item.ContentRef);
            }

            SBM.IC.Items.Clear();

            //update page count
            SBM.SP.TotalPages = SBM.GM.GetUsedPages();

            TriggerSaveItemsDelayed();
        }

        private SBItem PrepareFile(string File)
        {
            try
            {
                BitmapSource bmps;
                string Name;
                string Path;

                if (Uri.IsWellFormedUriString(File, UriKind.Absolute))
                {
                    //link
                    Name = File;
                    Path = File;

                    if (Name == "")
                        return null;

                    bmps = MiscUtils.GetFileThumbnail(File);
                }
                else if ((System.IO.File.GetAttributes(File) & System.IO.FileAttributes.Directory) == System.IO.FileAttributes.Directory)
                {
                    //folder
                    string folder = File;

                    Name = new DirectoryInfo(folder).Name; // folder.Substring(folder.LastIndexOf('\\') + 1);
                    Path = folder;

                    if (Name == "")
                        return null;

                    bmps = MiscUtils.GetFileThumbnail(folder);
                }
                else
                {
                    //file
                    string file = File;
                    string Extension = System.IO.Path.GetExtension(file).ToLower();

                    Name = System.IO.Path.GetFileNameWithoutExtension(File);
                    Path = file;

                    //cache lnk files
                    if (Extension == ".lnk")
                    {
                        string cacheDir = PortabilityManager.LinkCachePath;

                        if (!Directory.Exists(cacheDir))
                        {
                            Directory.CreateDirectory(cacheDir);
                        }

                        string guid = Guid.NewGuid().ToString();
                        string cacheFile = System.IO.Path.Combine(cacheDir, guid + ".lnk");

                        System.IO.File.Copy(file, cacheFile);

                        Path = guid + ".lnk";
                    }

                    if (Name == "")
                        return null;

                    bmps = MiscUtils.GetFileThumbnail(File);
                }

                return new SBItem(Name, "", "", Path, null, "", bmps);
            }
            catch (Exception ex)
            {
                CrashReporter.Report(ex);
                MessageBox.Show(ex.Message);

                return null;
            }
        }

        private void AddFile(string File)
        {
            DeactivateSearch();

            var item = PrepareFile(File);

            if (item == null)
                return;

            SBM.AddItem(item, (int)SBM.SP.CurrentPage, -1);
        }
    }
}
