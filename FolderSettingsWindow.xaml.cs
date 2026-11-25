using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Papochka {
    public partial class FolderSettingsWindow : Window {
        private readonly MainViewModel _vm;
        private double _hue = 0;          // 0..360
        private double _saturation = 0;   // 0..1
        private double _lightness = 0.12; // 0..1
        private bool _isDraggingSatHue;
        private bool _isDraggingVal;

        private bool _isClosing;

        private void SafeClose() {
            if (_isClosing)
                return;
            _isClosing = true;
            Close();
        }

        public FolderSettingsWindow(MainViewModel vm) {
            InitializeComponent();
            _vm = vm;
            DataContext = _vm;
            Loaded += FolderSettingsWindow_Loaded;
            PreviewKeyDown += FolderSettingsWindow_PreviewKeyDown;
            Deactivated += (s, e) => SafeClose();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDraggingSatHue && !_isDraggingVal)
                DragMove();
        }

        private void SatHue_MouseDown(object sender, MouseButtonEventArgs e) {
            SaveStateForUndo();
            _isDraggingSatHue = true;
            SatHueCanvas.CaptureMouse();
            UpdateSatHue(e.GetPosition(SatHueCanvas));
            e.Handled = true;
        }

        private void SatHue_MouseMove(object sender, MouseEventArgs e) {
            if (_isDraggingSatHue)
                UpdateSatHue(e.GetPosition(SatHueCanvas));
        }

        private void Val_MouseDown(object sender, MouseButtonEventArgs e) {
            SaveStateForUndo();
            _isDraggingVal = true;
            ValCanvas.CaptureMouse();
            UpdateVal(e.GetPosition(ValCanvas));
            e.Handled = true;
        }

        private void Val_MouseMove(object sender, MouseEventArgs e) {
            if (_isDraggingVal)
                UpdateVal(e.GetPosition(ValCanvas));
        }

        private void Picker_MouseUp(object sender, MouseButtonEventArgs e) {
            _isDraggingSatHue = false;
            _isDraggingVal = false;
            SatHueCanvas.ReleaseMouseCapture();
            ValCanvas.ReleaseMouseCapture();
        }

        private void UpdateSatHue(Point pos) {
            double x = Math.Clamp(pos.X, 0, SatHueCanvas.ActualWidth);
            double y = Math.Clamp(pos.Y, 0, SatHueCanvas.ActualHeight);

            Canvas.SetLeft(SatHueCursor, x - 5);
            Canvas.SetTop(SatHueCursor, y - 5);

            _hue = (x / SatHueCanvas.ActualWidth) * 360.0;
            _saturation = 1.0 - (y / SatHueCanvas.ActualHeight);

            // Обновляем центральный цвет на полосе яркости
            ValPureColorStop.Color = HslToColor(_hue, _saturation, 0.5);
            ApplyColor();
        }

        private void UpdateVal(Point pos) {
            double y = Math.Clamp(pos.Y, 0, ValCanvas.ActualHeight);
            Canvas.SetTop(ValCursor, y - 2);

            _lightness = 1.0 - (y / ValCanvas.ActualHeight);
            ApplyColor();
        }

        private void ApplyColor() {
            Color c = HslToColor(_hue, _saturation, _lightness);
            _vm.BgColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private static Color HslToColor(double h, double s, double l) {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2.0;

            double r1 = 0, g1 = 0, b1 = 0;
            if (h < 60) { r1 = c; g1 = x; } else if (h < 120) { r1 = x; g1 = c; } else if (h < 180) { g1 = c; b1 = x; } else if (h < 240) { g1 = x; b1 = c; } else if (h < 300) { r1 = x; b1 = c; } else { r1 = c; b1 = x; }

            return Color.FromRgb(
                (byte)Math.Round((r1 + m) * 255),
                (byte)Math.Round((g1 + m) * 255),
                (byte)Math.Round((b1 + m) * 255));
        }

        private void Reset_Click(object sender, RoutedEventArgs e) {
            SaveStateForUndo();
            _vm.BgColor = "#1E1E1E";
            _vm.BgOpacity = 0.8;
            _vm.ExpandedSize = 400;
            _hue = 0;
            _saturation = 0;
            _lightness = 0.12;
            ValPureColorStop.Color = Color.FromRgb(30, 30, 30);
            _vm.FolderName = "Папочка";
            _vm.ShowFolderName = true;
            _vm.ExpandOnHover = false;
            _vm.HoverOpenDelayMs = 300;
            _vm.HoverCloseDelayMs = 400;
        }

        private void Close_Click(object sender, RoutedEventArgs e) {
            Close();
        }

        private void FolderSettingsWindow_Loaded(object sender, RoutedEventArgs e) {
            SyncCursorsFromColor(_vm.BgColor);
            this.Focus();
        }

        private void SyncCursorsFromColor(string hexColor) {
            try {
                var c = (Color)ColorConverter.ConvertFromString(hexColor);
                ColorToHsl(c, out _hue, out _saturation, out _lightness);

                double x = (_hue / 360.0) * SatHueCanvas.ActualWidth;
                double y = (1.0 - _saturation) * SatHueCanvas.ActualHeight;
                Canvas.SetLeft(SatHueCursor, x - 5);
                Canvas.SetTop(SatHueCursor, y - 5);

                double valY = (1.0 - _lightness) * ValCanvas.ActualHeight;
                Canvas.SetTop(ValCursor, valY - 2);

                ValPureColorStop.Color = HslToColor(_hue, _saturation, 0.5);
            }
            catch { }
        }

        private static void ColorToHsl(Color color, out double h, out double s, out double l) {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;

            if (Math.Abs(max - min) < 0.0001) {
                h = 0;
                s = 0;
            } else {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

                if (max == r)
                    h = (g - b) / d + (g < b ? 6.0 : 0.0);
                else if (max == g)
                    h = (b - r) / d + 2.0;
                else
                    h = (r - g) / d + 4.0;

                h *= 60.0;
            }
        }

        private readonly Stack<(string Color, double Opacity, double Size, int MiniCols, int MiniRows, bool ShowName, 
                                string Name, bool ExpandOnHover, int OpenDelay, int CloseDelay)> _history = new();
        private bool _isUndoing = false;

        private void SaveStateForUndo() {
            if (_isUndoing)
                return;

            var current = (_vm.BgColor, _vm.BgOpacity, _vm.ExpandedSize, _vm.MiniCols, _vm.MiniRows, 
                            _vm.ShowFolderName, _vm.FolderName, _vm.ExpandOnHover, _vm.HoverOpenDelayMs, _vm.HoverCloseDelayMs);
            if (_history.Count == 0 || _history.Peek() != current) {
                _history.Push(current);
                UndoButton.IsEnabled = true;
            }
        }
        private void Undo_Click(object sender, RoutedEventArgs e) {
            if (_history.Count == 0)
                return;

            _isUndoing = true;
            try {
                var prev = _history.Pop();
                _vm.BgColor = prev.Color;
                _vm.BgOpacity = prev.Opacity;
                _vm.ExpandedSize = prev.Size;
                _vm.MiniCols = prev.MiniCols;
                _vm.MiniRows = prev.MiniRows;
                _vm.ShowFolderName = prev.ShowName;
                _vm.FolderName = prev.Name;
                _vm.ExpandOnHover = prev.ExpandOnHover;
                _vm.HoverOpenDelayMs = prev.OpenDelay;
                _vm.HoverCloseDelayMs = prev.CloseDelay;
                SyncCursorsFromColor(prev.Color);
            }
            finally {
                _isUndoing = false;
                UndoButton.IsEnabled = _history.Count > 0;
            }
        }


        private void FolderSettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                SafeClose();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) {
                if (Keyboard.FocusedElement is TextBox) {
                    FocusManager.SetFocusedElement(this, this);
                }

                Undo_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }


        private void Slider_MouseDown(object sender, MouseButtonEventArgs e) {
            SaveStateForUndo();
        }

        private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void MiniSizeInput_MouseDown(object sender, MouseButtonEventArgs e) {
            SaveStateForUndo();
            if (sender is TextBox tb && !tb.IsKeyboardFocusWithin) {
                tb.Focus();
                tb.SelectAll();
                e.Handled = true;
            }
        }

        private void CheckBox_MouseDown(object sender, MouseButtonEventArgs e) {
            SaveStateForUndo();
        }

        private void MiniSize_MouseWheel(object sender, MouseWheelEventArgs e) {
            SaveStateForUndo();
            int delta = e.Delta > 0 ? 1 : -1;
            if (sender == MiniColsBox)
                _vm.MiniCols += delta;
            else if (sender == MiniRowsBox)
                _vm.MiniRows += delta;
            e.Handled = true;
        }

        private void NameInput_MouseDown(object sender, MouseButtonEventArgs e) {
            SaveStateForUndo();
        }

        private void DelayInput_MouseWheel(object sender, MouseWheelEventArgs e) {
            SaveStateForUndo();
            int delta = e.Delta > 0 ? 50 : -50;
            if (sender == OpenDelayBox)
                _vm.HoverOpenDelayMs += delta;
            else if (sender == CloseDelayBox)
                _vm.HoverCloseDelayMs += delta;
            e.Handled = true;
        }

    }
}
