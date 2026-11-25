using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;
using System.Windows;

namespace Papochka {
    public class MainViewModel : INotifyPropertyChanged {
        public ObservableCollection<AppItem> Items { get; set; } = new();
        public ICommand Command { get; }
        public ICommand ToggleExpandCommand { get; }
        public ICommand RemoveItemCommand { get; }

        public double _windowLeft = 100;
        public double WindowLeft {
            get => _windowLeft;
            set { _windowLeft = value; Save(); }
        }

        public double _windowTop = 100;
        public double WindowTop {
            get => _windowTop;
            set { _windowTop = value; Save(); }
        }


        bool _isExpanded;
        public bool IsExpanded {
            get { return _isExpanded; }
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public FolderData Data { get; }

        public string FolderName {
            get => Data.Name;
            set {
                if (Data.Name != value) {
                    Data.Name = value;
                    OnPropertyChanged(nameof(FolderName));
                    Save();
                }
            }
        }

        public bool ShowFolderName {
            get => Data.ShowFolderName;
            set {
                if (Data.ShowFolderName != value) {
                    Data.ShowFolderName = value;
                    OnPropertyChanged(nameof(ShowFolderName));
                    Save();
                }
            }
        }

        public MainViewModel(FolderData data) {
            Data = data;
            _windowLeft = data.Left;
            _windowTop = data.Top;

            Command = new RelayCommand(LaunchApp);
            ToggleExpandCommand = new RelayCommand(SizeSwitch);
            RemoveItemCommand = new RelayCommand(RemoveItem);

            foreach (var item in data.Items) {
                if (File.Exists(item.Path) || Directory.Exists(item.Path)) {
                    item.Icon = IconHelper.GetIcon(item.Path);
                    Items.Add(item);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void AddItem(string[] paths) {
            bool addedAny = false;

            foreach (var rawPath in paths) {
                if (!File.Exists(rawPath) && !Directory.Exists(rawPath))
                    continue;

                string itemName = Path.GetFileNameWithoutExtension(rawPath);
                if (Items.Any(x => string.Equals(x.Name, itemName, StringComparison.OrdinalIgnoreCase) && FolderManager.IsInStorage(x.Path)))
                    continue;

                if (!Items.Any(x => x.Path == rawPath)) {
                    var item = new AppItem {
                        Name = itemName,
                        Path = rawPath,
                        IsTransferring = true
                    };
                    Items.Add(item);
                    addedAny = true;

                    _ = Task.Run(() =>
                    {
                        string movedPath = FolderManager.MoveToStorageIfDesktop(rawPath);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            item.Path = movedPath;
                            item.Icon = IconHelper.GetIcon(movedPath);
                            item.IsTransferring = false;
                            OnPropertyChanged(nameof(PrevItems));
                            Save();
                        });
                    });
                }
            }

            if (addedAny) {
                OnPropertyChanged(nameof(PrevItems));
            }
        }

        void LaunchApp(object? paramert) {
            if (paramert is AppItem app) {
                try {
                    Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
                }
                catch (Exception e) {

                }
            }
        }

        void SizeSwitch(object? parameter) {
            IsExpanded = !IsExpanded;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        public void Save() {
            Data.Left = _windowLeft;
            Data.Top = _windowTop;
            Data.Items = Items.ToList();
            FolderManager.SaveAll();
        }

        public void RemoveWithoutMoving(AppItem item) {
            Items.Remove(item);
            OnPropertyChanged(nameof(PrevItems));
            Save();
        }

        public void Load() {
            if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "folder.json"))) {
                var json = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "folder.json"));
                var data = JsonSerializer.Deserialize<FolderData>(json);
                WindowLeft = data.Left;
                WindowTop = data.Top;
                foreach (var item in data.Items) {
                    if (File.Exists(item.Path) || Directory.Exists(item.Path)) {
                        item.Icon = IconHelper.GetIcon(item.Path);
                        Items.Add(item);
                    }
                }
                OnPropertyChanged(nameof(PrevItems));
            }
        }

        private void RemoveItem(object? parameter) {
            if (parameter is AppItem app) {
                FolderManager.ReturnToDesktopIfInStorage(app.Path);
                Items.Remove(app);
                OnPropertyChanged(nameof(PrevItems));
                Save();
            }
        }

        public void MoveItem(AppItem source, AppItem target) {
            if (source == target)
                return;

            var oldItem = Items.IndexOf(source);
            var newItem = Items.IndexOf(target);

            if (oldItem != -1 && newItem != -1) {
                (Items[oldItem], Items[newItem]) = (Items[newItem], Items[oldItem]);
                OnPropertyChanged(nameof(PrevItems));
                Save();
            }
        }

        public string BgColor {
            get => Data.BgColor;
            set {
                if (Data.BgColor != value) {
                    Data.BgColor = value;
                    OnPropertyChanged(nameof(BgColor));
                    OnPropertyChanged(nameof(FolderBackgroundBrush));
                    Save();
                }
            }
        }

        public double BgOpacity {
            get => Data.BgOpacity;
            set {
                if (Math.Abs(Data.BgOpacity - value) > 0.001) {
                    Data.BgOpacity = value;
                    OnPropertyChanged(nameof(BgOpacity));
                    OnPropertyChanged(nameof(FolderBackgroundBrush));
                    Save();
                }
            }
        }

        public double ExpandedSize {
            get => Data.ExpandedSize;
            set {
                if (Math.Abs(Data.ExpandedSize - value) > 0.5) {
                    Data.ExpandedSize = value;
                    OnPropertyChanged(nameof(ExpandedSize));
                    Save();
                }
            }
        }

        public System.Windows.Media.Brush FolderBackgroundBrush {
            get {
                try {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(BgColor);
                    byte alpha = (byte)(Math.Clamp(BgOpacity, 0.1, 1.0) * 255);
                    return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
                }
                catch {
                    return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(204, 30, 30, 30));
                }
            }
        }

        public int MiniCols {
            get => Data.MiniCols;
            set {
                int clamped = Math.Clamp(value, 1, 10);
                if (Data.MiniCols != clamped) {
                    Data.MiniCols = clamped;
                    UpdateMiniLayout();
                    Save();
                }
            }
        }

        public int MiniRows {
            get => Data.MiniRows;
            set {
                int clamped = Math.Clamp(value, 1, 10);
                if (Data.MiniRows != clamped) {
                    Data.MiniRows = clamped;
                    UpdateMiniLayout();
                    Save();
                }
            }
        }

        public double MiniBoxWidth => MiniCols == 1 ? 64 : (MiniCols * 80 - 16);
        public double MiniBoxHeight => MiniRows == 1 ? 64 : (MiniRows * 85 - 21);
        public double MiniOuterWidth => MiniCols * 80 - 4;

        public int MiniPreviewRows => (MiniCols < 2 || MiniRows < 2) ? (MiniCols > MiniRows ? 1 : (MiniRows > MiniCols ? 4 : 2)) : MiniRows;
        public int MiniPreviewCols => (MiniCols < 2 || MiniRows < 2) ? (MiniCols > MiniRows ? 4 : (MiniRows > MiniCols ? 1 : 2)) : MiniCols;
        public IEnumerable<AppItem> PrevItems => Items.Take(MiniPreviewRows * MiniPreviewCols);
        public double MiniIconSize => (MiniCols >= 2 && MiniRows >= 2) ? 38 : (MiniCols > 1 ? 26 : 20);

        private void UpdateMiniLayout() {
            OnPropertyChanged(nameof(MiniCols));
            OnPropertyChanged(nameof(MiniRows));
            OnPropertyChanged(nameof(MiniBoxWidth));
            OnPropertyChanged(nameof(MiniBoxHeight));
            OnPropertyChanged(nameof(MiniOuterWidth));
            OnPropertyChanged(nameof(MiniPreviewRows));
            OnPropertyChanged(nameof(MiniPreviewCols));
            OnPropertyChanged(nameof(PrevItems));
            OnPropertyChanged(nameof(MiniIconSize));
        }

        public bool QuickLaunchFromMini {
            get => Data.QuickLaunchFromMini;
            set {
                if (Data.QuickLaunchFromMini != value) {
                    Data.QuickLaunchFromMini = value;
                    OnPropertyChanged(nameof(QuickLaunchFromMini));
                    Save();
                }
            }
        }

        public bool ExpandOnHover {
            get => Data.ExpandOnHover;
            set {
                if (Data.ExpandOnHover != value) {
                    Data.ExpandOnHover = value;
                    OnPropertyChanged(nameof(ExpandOnHover));
                    Save();
                }
            }
        }

        public int HoverOpenDelayMs {
            get => Data.HoverOpenDelayMs;
            set {
                int clamped = Math.Clamp(value, 0, 5000);
                if (Data.HoverOpenDelayMs != clamped) {
                    Data.HoverOpenDelayMs = clamped;
                    OnPropertyChanged(nameof(HoverOpenDelayMs));
                    Save();
                }
            }
        }

        public int HoverCloseDelayMs {
            get => Data.HoverCloseDelayMs;
            set {
                int clamped = Math.Clamp(value, 0, 5000);
                if (Data.HoverCloseDelayMs != clamped) {
                    Data.HoverCloseDelayMs = clamped;
                    OnPropertyChanged(nameof(HoverCloseDelayMs));
                    Save();
                }
            }
        }
    }
}
