using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DragEventArgs = System.Windows.DragEventArgs;
using Point = System.Windows.Point;

namespace Papochka;

public partial class MainWindow : Window {
    public MainViewModel _viewModel;
    private readonly DesktopHelper _desktopHelper = new();

    private Point _startPoint;
    private Point _tileStartPoint;
    private bool _canDragWindow;
    private bool _isDragging;
    private bool _isTileDragging;
    private bool _isShellMenuOpen;

    public MainWindow(FolderData folderData) {
        InitializeComponent();
        _viewModel = new MainViewModel(folderData);
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Left = _viewModel.WindowLeft;
        Top = _viewModel.WindowTop;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        _desktopHelper.AttachToDesktop(this);
    }

    bool _isSettingsOpen;

    private void Window_Deactivated(object sender, EventArgs e) {
        if (!_isSettingsOpen)
            _viewModel.IsExpanded = false;
    }

    protected override void OnClosed(EventArgs e) {
        _desktopHelper?.Detach();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Items.Clear();
        DataContext = null;

        base.OnClosed(e);
    }

    private void Border_Drop(object sender, DragEventArgs e) {
        if (_isTileDragging)
            return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            _ = Task.Delay(80).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => _viewModel.AddItem(files));
            });
        }
    }


    private async void Border_MouseLeave(object sender, MouseEventArgs e) {
        _hoverOpenCts?.Cancel();

        await Task.Delay(_viewModel.HoverCloseDelayMs);
        if (!this.IsMouseOver && !_isSettingsOpen && !_isTileDragging && !_isShellMenuOpen && !TitleBox.IsKeyboardFocusWithin) {
            _viewModel.IsExpanded = false;
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        _isDragging = false;
        _startPoint = e.GetPosition(this);
        _canDragWindow = true;
        if (TitleBox.IsKeyboardFocusWithin) {
            Keyboard.ClearFocus();
        }
    }

    private void Border_MouseMove(object sender, MouseEventArgs e) {
        if (_viewModel.IsExpanded)
            return;

        if (e.LeftButton == MouseButtonState.Pressed && !_isDragging && !_isTileDragging) {
            Point currentPos = e.GetPosition(this);
            if ((Math.Abs(currentPos.X - _startPoint.X) > 10 || Math.Abs(currentPos.Y - _startPoint.Y) > 10) && _canDragWindow) {
                _isDragging = true;
                DragMove();
                SnapToGridAndSave();
            }
        }
    }

    private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (!_isDragging && !_isTileDragging) {
            _viewModel.IsExpanded = true;
        }
        _isDragging = false;
        _canDragWindow = false;
    }

    private void SnapToGridAndSave() {
        if (_viewModel.IsExpanded)
            return;

        var (gridX, gridY) = DesktopHelper.GetDesktopGridSize(this);
        Rect workArea = DesktopHelper.GetWorkAreaAt(Left + 35, Top + 35, this);

        double offsetY = 5;
        double maxLeft = workArea.Right - (gridX * _viewModel.MiniCols);
        double maxTop = workArea.Bottom - (gridY * _viewModel.MiniRows);

        Left = workArea.Left + Math.Round((Left - workArea.Left) / gridX) * gridX;
        Top = workArea.Top + Math.Round((Top - workArea.Top) / gridY) * gridY + offsetY;

        Left = Math.Clamp(Left, workArea.Left, maxLeft);
        Top = Math.Clamp(Top, workArea.Top + offsetY, maxTop);

        _viewModel.WindowLeft = Left;
        _viewModel.WindowTop = Top;
    }

    private async void Tile_RightClick(object sender, MouseButtonEventArgs e) {
        if (sender is FrameworkElement element && element.DataContext is AppItem item) {
            e.Handled = true;

            Point screenPos = PointToScreen(e.GetPosition(this));
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

            _isShellMenuOpen = true;
            try {
                bool removeFromFolder = ShellContextMenuHelper.ShowContextMenu(hwnd, item.Path, screenPos);
                if (removeFromFolder || (!File.Exists(item.Path) && !Directory.Exists(item.Path))) {
                    _viewModel.RemoveItemCommand.Execute(item);
                }
            }
            finally {
                await Task.Delay(1400);
                _isShellMenuOpen = false;
                if (!IsMouseOver) {
                    _viewModel.IsExpanded = false;
                }
            }
        }
    }

    private void Tile_MouseDown(object sender, MouseButtonEventArgs e) {
        if (sender is Border b && b.DataContext is AppItem appItem && appItem.IsTransferring) {
            e.Handled = true;
            return;
        }
        _canDragWindow = false;
        _tileStartPoint = e.GetPosition(this);
        _isTileDragging = false;
        if (sender is Border border) {
            border.CaptureMouse();
        }
        e.Handled = true;
    }

    private void Tile_MouseMove(object sender, MouseEventArgs e) {
        if (e.LeftButton == MouseButtonState.Pressed && sender is Border border && border.IsMouseCaptured) {
            Point currentPos = e.GetPosition(this);
            if (!_isTileDragging && (Math.Abs(currentPos.X - _tileStartPoint.X) > 4 || Math.Abs(currentPos.Y - _tileStartPoint.Y) > 4)) {
                _isTileDragging = true;
                border.Opacity = 0.5;
            }
            e.Handled = true;
        }
    }

    private void Tile_MouseUp(object sender, MouseButtonEventArgs e) {
        if (sender is Border border && border.DataContext is AppItem item) {
            border.ReleaseMouseCapture();
            border.Opacity = 1.0;

            if (!_isTileDragging) {
                _viewModel.Command.Execute(item);
            } else {
                Point endPos = e.GetPosition(this);
                bool isOutside = endPos.X < 0 || endPos.Y < 0 || endPos.X > ActualWidth || endPos.Y > ActualHeight;

                if (isOutside) {
                    Point screenDropPos = PointToScreen(endPos);

                    string desktopFilePath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        System.IO.Path.GetFileName(item.Path));

                    FolderManager.ReturnToDesktopIfInStorage(item.Path);
                    _viewModel.RemoveWithoutMoving(item);
                    _viewModel.IsExpanded = false;

                    _ = DesktopHelper.MoveDesktopIconToAsync(desktopFilePath, (int)screenDropPos.X, (int)screenDropPos.Y);
                } else {
                    var hit = System.Windows.Media.VisualTreeHelper.HitTest(this, endPos);
                    DependencyObject? current = hit?.VisualHit;
                    while (current != null && current is not Border) {
                        current = System.Windows.Media.VisualTreeHelper.GetParent(current);
                    }

                    if (current is Border targetBorder && targetBorder.DataContext is AppItem targetItem && targetItem != item) {
                        _viewModel.MoveItem(item, targetItem);
                    }
                }
            }

            _isTileDragging = false;
            e.Handled = true;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.IsExpanded)) {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

            if (_viewModel.IsExpanded) {
                Rect workArea = DesktopHelper.GetWorkAreaAt(_viewModel.WindowLeft + 35, _viewModel.WindowTop + 35, this);
                double size = _viewModel.ExpandedSize;

                double targetLeft = _viewModel.WindowLeft;
                double targetTop = _viewModel.WindowTop;

                if (targetLeft + size > workArea.Right)
                    targetLeft = workArea.Right - size - 10;
                if (targetLeft < workArea.Left)
                    targetLeft = workArea.Left + 10;

                if (targetTop + size > workArea.Bottom)
                    targetTop = workArea.Bottom - size - 10;
                if (targetTop < workArea.Top)
                    targetTop = workArea.Top + 10;

                Left = targetLeft;
                Top = targetTop;

                DesktopHelper.IsSystemDialogOpening = true;
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                DesktopHelper.IsSystemDialogOpening = false;
            } else {
                Left = _viewModel.WindowLeft;
                Top = _viewModel.WindowTop;
                SetWindowPos(hwnd, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }
        } else if (e.PropertyName == nameof(MainViewModel.MiniCols) || e.PropertyName == nameof(MainViewModel.MiniRows)) {
            SnapToGridAndSave();
        }
    }
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);


    private void MiniFolder_RightClick(object sender, MouseButtonEventArgs e) {
        _hoverOpenCts?.Cancel();

        Point screenPos = PointToScreen(e.GetPosition(this));
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        int action = 0;
        _isShellMenuOpen = true;
        try {
            action = ShellContextMenuHelper.ShowMiniFolderMenu(hwnd, screenPos);
        }
        finally {
            _isShellMenuOpen = false;
        }

        if (action == 1) {
            _isSettingsOpen = true;
            _viewModel.IsExpanded = true;

            var settingsWin = new FolderSettingsWindow(_viewModel);
            settingsWin.Closed += (s, args) =>
            {
                _isSettingsOpen = false;
                _viewModel.IsExpanded = false;
                SnapToGridAndSave();
            };
            settingsWin.Show();
        } else if (action == 2) {
            FolderManager.DeleteFolder(_viewModel.Data, this);
        }

        e.Handled = true;
    }


    private async void BigBorder_RightClick(object sender, MouseButtonEventArgs e) {
        Point screenPos = PointToScreen(e.GetPosition(this));
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _isShellMenuOpen = true;
        try {
            ShellContextMenuHelper.ShowBackgroundContextMenu(hwnd, desktopPath, screenPos, newFilePath => {
                Dispatcher.Invoke(() => _viewModel.AddItem([newFilePath]));
            });
        }
        finally {
            await Task.Delay(500);
            _isShellMenuOpen = false;
            if (!IsMouseOver) {
                _viewModel.IsExpanded = false;
            }
        }
        e.Handled = true;
    }
    private void TitleBox_MouseDown(object sender, MouseButtonEventArgs e) {
        _desktopHelper.EnableKeyboardInput(this);
        if (sender is TextBox tb) {
            tb.Focus();
            tb.SelectAll();
        }
        e.Handled = true;
    }
    private void TitleBox_LostFocus(object sender, RoutedEventArgs e) {
        _desktopHelper.DisableKeyboardInput();
    }
    private void TitleBox_KeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Enter) {
            _desktopHelper.DisableKeyboardInput();
            FocusManager.SetFocusedElement(this, null);
            Keyboard.ClearFocus();
        }
    }
    private void MiniItem_MouseUp(object sender, MouseButtonEventArgs e) {
        if (!_viewModel.QuickLaunchFromMini || _isDragging)
            return;
        if (sender is FrameworkElement fe && fe.DataContext is AppItem item) {
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.Path) {
                    UseShellExecute = true
                });
            }
            catch { }
            _isDragging = false;
            e.Handled = true;
        }
    }

    private System.Threading.CancellationTokenSource? _hoverOpenCts;

    private async void Border_MouseEnter(object sender, MouseEventArgs e) {
        if (!_viewModel.ExpandOnHover || _viewModel.IsExpanded || _isDragging || _isShellMenuOpen || Mouse.RightButton == MouseButtonState.Pressed)
            return;

        _hoverOpenCts?.Cancel();
        _hoverOpenCts = new System.Threading.CancellationTokenSource();
        var token = _hoverOpenCts.Token;

        try {
            await Task.Delay(_viewModel.HoverOpenDelayMs, token);
            if (!token.IsCancellationRequested && sender is Border border && border.IsMouseOver && !_isDragging && !_isShellMenuOpen && Mouse.RightButton != MouseButtonState.Pressed) {
                _viewModel.IsExpanded = true;
            }
        }
        catch (TaskCanceledException) { }
    }

}
