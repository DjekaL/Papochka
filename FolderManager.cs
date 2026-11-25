using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Linq;
using System.Runtime.InteropServices;

namespace Papochka {
    public static class FolderManager {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "folders.json");

        public static List<FolderData> AllFolders { get; private set; } = new();

        public static void LoadAll() {
            if (File.Exists(FilePath)) {
                try {
                    string json = File.ReadAllText(FilePath);
                    AllFolders = JsonSerializer.Deserialize<List<FolderData>>(json) ?? new List<FolderData>();
                }
                catch {
                    AllFolders = new List<FolderData>();
                }
            }
            StartStorageWatcher();
        }

        public static void SaveAll() {
            var json = JsonSerializer.Serialize(AllFolders, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }

        public static void CreateNewFolder() {
            var anyWindow = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            double scaleX = 1.0, scaleY = 1.0;
            if (anyWindow != null) {
                var source = System.Windows.PresentationSource.FromVisual(anyWindow);
                if (source?.CompositionTarget != null) {
                    scaleX = source.CompositionTarget.TransformToDevice.M11;
                    scaleY = source.CompositionTarget.TransformToDevice.M22;
                }
            }

            double dipClickX = MouseHookHelper.LastRightClickX / scaleX;
            double dipClickY = MouseHookHelper.LastRightClickY / scaleY;

            var (gridX, gridY) = anyWindow != null ? DesktopHelper.GetDesktopGridSize(anyWindow) : (80.0, 85.0);
            Rect workArea = DesktopHelper.GetWorkAreaAt(dipClickX, dipClickY, anyWindow);

            double offsetY = 5;
            double left = workArea.Left + Math.Round((dipClickX - 35 - workArea.Left) / gridX) * gridX;
            double top = workArea.Top + Math.Round((dipClickY - 35 - workArea.Top) / gridY) * gridY + offsetY;

            double maxLeft = workArea.Right - gridX;
            double maxTop = workArea.Bottom - gridY;

            left = Math.Clamp(left, workArea.Left, maxLeft);
            top = Math.Clamp(top, workArea.Top + offsetY, maxTop);

            var newFolder = new FolderData { Left = left, Top = top };
            AllFolders.Add(newFolder);
            SaveAll();

            new MainWindow(newFolder).Show();
        }

        public static void DeleteFolder(FolderData folder, MainWindow window) {
            foreach (var item in folder.Items) {
                ReturnToDesktopIfInStorage(item.Path);
            }

            AllFolders.Remove(folder);
            SaveAll();
            window.Close();
        }


        public static void RegisterDesktopContextMenu() {
            string exePath = Process.GetCurrentProcess().MainModule!.FileName;

            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\DesktopBackground\Shell\PapochkaNewFolder", false);
            string keyPath = @"Software\Classes\DesktopBackground\Shell\Z_PapochkaNewFolder";
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue("", "Создать Папочку");
            key.SetValue("Icon", $"\"{exePath}\",0");
            key.SetValue("SeparatorAfter", "");
            //key.SetValue("Icon", $"\"{exePath}\",0"); // Иконка вашей программы рядом с текстом

            using var commandKey = key.CreateSubKey("command");
            commandKey.SetValue("", $"\"{exePath}\" --new");
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        private static readonly string StoragePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage");

        // Перемещает файл/папку с рабочего стола в Storage и возвращает новый путь
        public static string MoveToStorageIfDesktop(string sourcePath) {
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string? parentDir = Path.GetDirectoryName(sourcePath);

            bool isFromDesktop = !string.IsNullOrEmpty(parentDir) && (
                string.Equals(parentDir, userDesktop, StringComparison.OrdinalIgnoreCase) ||
                parentDir.EndsWith(@"\Desktop", StringComparison.OrdinalIgnoreCase) ||
                parentDir.EndsWith(@"\Рабочий стол", StringComparison.OrdinalIgnoreCase));


            if (isFromDesktop) {
                Directory.CreateDirectory(StoragePath);
                string fileName = Path.GetFileName(sourcePath);
                string destPath = Path.Combine(StoragePath, fileName);

                int counter = 1;
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                while (File.Exists(destPath) || Directory.Exists(destPath)) {
                    destPath = Path.Combine(StoragePath, $"{nameWithoutExt} ({counter++}){ext}");
                }

                try {
                    if (File.Exists(sourcePath)) {
                        File.Move(sourcePath, destPath);
                        NotifyDesktopChanged(sourcePath);
                        return destPath;
                    }
                    if (Directory.Exists(sourcePath)) {
                        Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(sourcePath, destPath);
                        NotifyDesktopChanged(sourcePath);
                        return destPath;
                    }
                }
                catch (UnauthorizedAccessException) {
                    try {
                        string safeSrc = sourcePath.Replace("'", "''");
                        string safeDst = destPath.Replace("'", "''");

                        var psi = new System.Diagnostics.ProcessStartInfo {
                            FileName = "powershell.exe",
                            Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Move-Item -LiteralPath '{safeSrc}' -Destination '{safeDst}' -Force; icacls '{safeDst}' /reset\"",
                            Verb = "runas",
                            UseShellExecute = true,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        };

                        using var proc = System.Diagnostics.Process.Start(psi);
                        proc?.WaitForExit();

                        if (File.Exists(destPath) || Directory.Exists(destPath))
                            return destPath;
                    }
                    catch { }
                }
                catch {
                }
            }
            return sourcePath;
        }

        private static void NotifyDesktopChanged(string movedSourcePath) {
            IntPtr filePtr = Marshal.StringToHGlobalUni(movedSourcePath);
            SHChangeNotify(0x00000004, 0x0005, filePtr, IntPtr.Zero); // SHCNE_DELETE (файл/папка удалены)
            Marshal.FreeHGlobal(filePtr);

            string? desktopDir = Path.GetDirectoryName(movedSourcePath);
            if (!string.IsNullOrEmpty(desktopDir)) {
                IntPtr dirPtr = Marshal.StringToHGlobalUni(desktopDir);
                SHChangeNotify(0x00001000, 0x0005, dirPtr, IntPtr.Zero); // SHCNE_UPDATEDIR (обновить рабочий стол сразу!)
                Marshal.FreeHGlobal(dirPtr);
            }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        public static void ReturnToDesktopIfInStorage(string storedPath) {
            string? parentDir = Path.GetDirectoryName(storedPath);
            if (!string.Equals(parentDir, StoragePath, StringComparison.OrdinalIgnoreCase))
                return;

            if (!File.Exists(storedPath) && !Directory.Exists(storedPath))
                return;

            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string fileName = Path.GetFileName(storedPath);
            string destPath = Path.Combine(userDesktop, fileName);

            int counter = 1;
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            while (File.Exists(destPath) || Directory.Exists(destPath)) {
                destPath = Path.Combine(userDesktop, $"{nameWithoutExt} ({counter++}){ext}");
            }

            try {
                if (File.Exists(storedPath))
                    Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(storedPath, destPath);
                else if (Directory.Exists(storedPath))
                    Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(storedPath, destPath);

            }
            catch { }
        }

        public static bool IsInStorage(string path) => string.Equals(Path.GetDirectoryName(path), StoragePath, StringComparison.OrdinalIgnoreCase);

        private static FileSystemWatcher? _storageWatcher;

        public static void StartStorageWatcher() {
            Directory.CreateDirectory(StoragePath);

            _storageWatcher = new FileSystemWatcher(StoragePath) {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _storageWatcher.Deleted += async (s, e) =>
            {
                await Task.Delay(200);
                if (File.Exists(e.FullPath) || Directory.Exists(e.FullPath))
                    return;

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var win in System.Windows.Application.Current.Windows.OfType<MainWindow>()) {
                        var item = win._viewModel.Items.FirstOrDefault(x =>
                            string.Equals(x.Path, e.FullPath, StringComparison.OrdinalIgnoreCase));

                        if (item != null) {
                            win._viewModel.RemoveWithoutMoving(item);
                        }
                    }
                });
            };

            _storageWatcher.Renamed += (s, e) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var win in System.Windows.Application.Current.Windows.OfType<MainWindow>()) {
                        var item = win._viewModel.Items.FirstOrDefault(x =>
                            string.Equals(x.Path, e.OldFullPath, StringComparison.OrdinalIgnoreCase));

                        if (item != null) {
                            item.Path = e.FullPath;
                            item.Name = Path.GetFileNameWithoutExtension(e.FullPath);
                            win._viewModel.Save();
                        }
                    }
                });
            };
        }

    }
}
