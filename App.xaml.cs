using System.IO;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Papochka;

public partial class App : Application {
    private const string EventName = "Papochka_CreateNewFolder_Event";
    private const string AppRegistryName = "PapochkaDesktopWidget";
    private EventWaitHandle? _newFolderEvent;
    private Forms.NotifyIcon? _notifyIcon;

    private System.Windows.Threading.DispatcherTimer? _trayAnimTimer;
    private float _waveOffset = 0f;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);


    protected override void OnStartup(StartupEventArgs e) {

        SetPreferredAppMode(PreferredAppMode.AllowDark);
        base.OnStartup(e);

        if (e.Args.Contains("--new")) {
            if (EventWaitHandle.TryOpenExisting(EventName, out var existingEvent)) {
                existingEvent.Set();
                Shutdown();
                return;
            }
        }

        SetupTrayIcon();
        MouseHookHelper.Start();

        FolderManager.RegisterDesktopContextMenu();
        StartNewFolderListener();

        FolderManager.LoadAll();
        foreach (var folder in FolderManager.AllFolders) {
            new MainWindow(folder).Show();
        }
    }

    private void SetupTrayIcon() {
        _notifyIcon = new Forms.NotifyIcon {
            Icon = CreateGradientTrayIcon(0f),
            Visible = true,
            Text = "Папочка"
        };

        var menu = new Forms.ContextMenuStrip();

        var autostartItem = new Forms.ToolStripMenuItem("Запускать вместе с Windows") {
            Checked = IsAutostartEnabled(),
            CheckOnClick = true
        };
        autostartItem.Click += (s, e) => SetAutostart(autostartItem.Checked);
        menu.Items.Add(autostartItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (s, e) => Shutdown());

        _notifyIcon.ContextMenuStrip = menu;

        _trayAnimTimer = new System.Windows.Threading.DispatcherTimer {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        _trayAnimTimer.Tick += (s, e) =>
        {
            if (_notifyIcon == null)
                return;
            _waveOffset = (_waveOffset + 0.008f) % 1.0f;

            var oldIcon = _notifyIcon.Icon;
            _notifyIcon.Icon = CreateGradientTrayIcon(_waveOffset);
            oldIcon?.Dispose();
        };
        _trayAnimTimer.Start();
    }

    private static bool IsAutostartEnabled() {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue(AppRegistryName) != null;
    }

    private static void SetAutostart(bool enable) {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key == null)
            return;

        if (enable) {
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath)) {
                key.SetValue(AppRegistryName, $"\"{exePath}\"");
                // Обновляем путь в контекстном меню рабочего стола, если exe перенесли в другую папку
                FolderManager.RegisterDesktopContextMenu();
            }
        } else {
            key.DeleteValue(AppRegistryName, false);
        }
    }

    protected override void OnExit(ExitEventArgs e) {
        _trayAnimTimer?.Stop();
        if (_notifyIcon != null) {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        MouseHookHelper.Stop();
        base.OnExit(e);
    }

    private void StartNewFolderListener() {
        _newFolderEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        Task.Run(() => {
            while (_newFolderEvent.WaitOne()) {
                Dispatcher.Invoke(() => FolderManager.CreateNewFolder());
            }
        });
    }

    private enum PreferredAppMode {
        Default = 0,
        AllowDark = 1,
        ForceDark = 2,
        ForceLight = 3,
        Max = 4
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetPreferredAppMode(PreferredAppMode preferredAppMode);

    private static System.Drawing.Icon CreateGradientTrayIcon(float offset) {
        int size = 32;
        using var bmp = new System.Drawing.Bitmap(size, size);
        using (var g = System.Drawing.Graphics.FromImage(bmp)) {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            // Сильно скруглённый фон (r = 15 для размера 32x32)
            using var bgPath = new System.Drawing.Drawing2D.GraphicsPath();
            int r = 15;
            bgPath.AddArc(1, 1, r, r, 180, 90);
            bgPath.AddArc(size - r - 1, 1, r, r, 270, 90);
            bgPath.AddArc(size - r - 1, size - r - 1, r, r, 0, 90);
            bgPath.AddArc(1, size - r - 1, r, r, 90, 90);
            bgPath.CloseFigure();

            // Широкая волна (в 3 раза шире иконки) для максимально плавных переходов
            float waveWidth = size * 3f;
            float startX = -waveWidth + (offset * waveWidth);

            var p1 = new System.Drawing.PointF(startX, 0);
            var p2 = new System.Drawing.PointF(startX + waveWidth, 8);

            using var gradBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                p1,
                p2,
                System.Drawing.Color.FromArgb(255, 59, 130, 246),
                System.Drawing.Color.FromArgb(255, 59, 130, 246)) {
                WrapMode = System.Drawing.Drawing2D.WrapMode.Tile
            };

            // 9 богатых оттенков спектра, замкнутых в непрерывное кольцо
            gradBrush.InterpolationColors = new System.Drawing.Drawing2D.ColorBlend {
                Colors = new[]
                {
                System.Drawing.Color.FromArgb(255, 59, 130, 246),   // 1. Синий
                System.Drawing.Color.FromArgb(255, 139, 92, 246),   // 2. Индиго-фиолетовый
                System.Drawing.Color.FromArgb(255, 217, 70, 239),   // 3. Фуксия
                System.Drawing.Color.FromArgb(255, 244, 63, 94),    // 4. Малиново-красный
                System.Drawing.Color.FromArgb(255, 249, 115, 22),   // 5. Оранжевый
                System.Drawing.Color.FromArgb(255, 234, 179, 8),    // 6. Янтарно-жёлтый
                System.Drawing.Color.FromArgb(255, 132, 204, 22),   // 7. Лаймовый
                System.Drawing.Color.FromArgb(255, 16, 185, 129),   // 8. Изумрудный
                System.Drawing.Color.FromArgb(255, 6, 182, 212),    // 9. Бирюзовый
                System.Drawing.Color.FromArgb(255, 59, 130, 246)    // Замыкаем в синий
            },
                Positions = new[] { 0f, 0.11f, 0.22f, 0.33f, 0.44f, 0.55f, 0.66f, 0.77f, 0.88f, 1f }
            };

            g.FillPath(gradBrush, bgPath);

            // Вместо квадратов — аккуратный минималистичный силуэт папки со скруглёнными краями
            using var folderPath = new System.Drawing.Drawing2D.GraphicsPath();
            // Верхний язычок папки + основной прямоугольник (x: 7..25, y: 9..23)
            folderPath.AddArc(7, 9, 4, 4, 180, 90);      // левый верхний угол язычка
            folderPath.AddLine(9, 9, 14, 9);             // верх язычка
            folderPath.AddLine(14, 9, 16, 11.5f);        // скос язычка вниз
            folderPath.AddLine(16, 11.5f, 23, 11.5f);    // верх правой части
            folderPath.AddArc(21, 11.5f, 4, 4, 270, 90); // правый верхний угол
            folderPath.AddArc(21, 19, 4, 4, 0, 90);      // правый нижний угол
            folderPath.AddArc(7, 19, 4, 4, 90, 90);      // левый нижний угол
            folderPath.CloseFigure();

            using var folderBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(235, 255, 255, 255));
            g.FillPath(folderBrush, folderPath);
        }

        IntPtr hIcon = bmp.GetHicon();
        try {
            using var tempIcon = System.Drawing.Icon.FromHandle(hIcon);
            return (System.Drawing.Icon)tempIcon.Clone();
        }
        finally {
            DestroyIcon(hIcon);
        }
    }
}
