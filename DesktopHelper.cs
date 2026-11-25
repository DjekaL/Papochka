using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Papochka {
    public class DesktopHelper {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);


        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private WinEventDelegate? _winEventDelegate;
        private IntPtr _hook = IntPtr.Zero;
        private bool _isDesktopFocused = false;
        private IntPtr _handle = IntPtr.Zero;

        private bool _isEditingText = false;

        public static bool IsSystemDialogOpening { get; set; } = false;

        public void AttachToDesktop(Window window) {
            _handle = new WindowInteropHelper(window).Handle;
            int exStyle = GetWindowLong(_handle, GWL_EXSTYLE);

            SetWindowPos(_handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
            SetWindowLong(_handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

            _winEventDelegate = async (hook, eventType, hwnd, idObject, idChild, thread, time) => {
                StringBuilder className = new StringBuilder(256);
                GetClassName(hwnd, className, className.Capacity);
                string cls = className.ToString();

                if (cls == "WorkerW" || cls == "Progman" || eventType == 0x0016) {
                    _isDesktopFocused = true;
                    await Task.Delay(5);
                    ShowWindow(_handle, 4);
                    SetWindowPos(_handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                    SetWindowPos(_handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                } else if (eventType == 0x0003 && !_isEditingText && !IsSystemDialogOpening) {
                    _isDesktopFocused = false;
                    SetWindowPos(_handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                }
            };

            _hook = SetWinEventHook(0x0003, 0x0017, IntPtr.Zero, _winEventDelegate, 0, 0, 0x0000);
        }

        public void Detach() {
            if (_hook != IntPtr.Zero) {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }
            if (_handle != IntPtr.Zero) {
                HwndSource.FromHwnd(_handle)?.RemoveHook(WndProc);
            }
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
            const int WM_WINDOWPOSCHANGING = 0x0046;
            if (msg == WM_WINDOWPOSCHANGING && !_isDesktopFocused && !_isEditingText && !IsSystemDialogOpening) {
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                pos.hwndInsertAfter = HWND_BOTTOM;
                Marshal.StructureToPtr(pos, lParam, true);
            }
            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public void EnableKeyboardInput(Window window) {
            if (_handle == IntPtr.Zero)
                return;
            _isEditingText = true;

            int exStyle = GetWindowLong(_handle, GWL_EXSTYLE);
            SetWindowLong(_handle, GWL_EXSTYLE, exStyle & ~WS_EX_NOACTIVATE);

            SetWindowPos(_handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE);
            window.Activate();
            SetForegroundWindow(_handle);
        }

        public void DisableKeyboardInput() {
            if (_handle == IntPtr.Zero)
                return;
            _isEditingText = false;

            int exStyle = GetWindowLong(_handle, GWL_EXSTYLE);
            SetWindowLong(_handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
            SetWindowPos(_handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }

        public static (double gridX, double gridY) GetDesktopGridSize(System.Windows.Media.Visual visual) {
            // Находим системный ListView рабочего стола (SysListView32)
            IntPtr progman = FindWindow("Progman", null);
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (defView == IntPtr.Zero) {
                IntPtr workerW = IntPtr.Zero;
                while ((workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null)) != IntPtr.Zero) {
                    defView = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero)
                        break;
                }
            }

            IntPtr listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
            if (listView != IntPtr.Zero) {
                const uint LVM_GETITEMSPACING = 0x1033;
                int spacing = (int)SendMessage(listView, LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero);

                // Младшие 16 бит — ширина ячейки в физических пикселях, старшие 16 бит — высота
                int physX = spacing & 0xFFFF;
                int physY = (spacing >> 16) & 0xFFFF;

                // Переводим физические пиксели в логические единицы WPF (с учетом масштаба 100%/125%/150%)
                var source = PresentationSource.FromVisual(visual);
                double dpiX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
                double dpiY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

                if (physX > 0 && physY > 0)
                    return (physX * dpiX, physY * dpiY);
            }

            return (80, 85); // Запасной вариант, если рабочий стол скрыт
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT_WIN32 { public int X, Y; }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT_WIN32 pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        // Возвращает рабочую область (без панели задач) того монитора, где находится точка (dipX, dipY)
        public static Rect GetWorkAreaAt(double dipX, double dipY, System.Windows.Media.Visual? visual = null) {
            double scaleX = 1.0, scaleY = 1.0;
            if (visual != null) {
                var source = PresentationSource.FromVisual(visual);
                if (source?.CompositionTarget != null) {
                    scaleX = source.CompositionTarget.TransformToDevice.M11;
                    scaleY = source.CompositionTarget.TransformToDevice.M22;
                }
            }

            // Переводим логические координаты WPF в физические пиксели для Win32 API
            var pt = new POINT_WIN32 { X = (int)(dipX * scaleX), Y = (int)(dipY * scaleY) };
            const uint MONITOR_DEFAULTTONEAREST = 2;
            IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(hMonitor, ref mi)) {
                // Переводим физические границы монитора обратно в логические единицы WPF
                return new Rect(
                    mi.rcWork.Left / scaleX,
                    mi.rcWork.Top / scaleY,
                    (mi.rcWork.Right - mi.rcWork.Left) / scaleX,
                    (mi.rcWork.Bottom - mi.rcWork.Top) / scaleY);
            }

            return SystemParameters.WorkArea;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LVITEM {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
        }

        public static async Task MoveDesktopIconToAsync(string filePath, int screenX, int screenY) {
            string targetName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            IntPtr listView = FindDesktopListView();
            if (listView == IntPtr.Zero)
                return;

            GetWindowThreadProcessId(listView, out uint processId);
            IntPtr hProcess = OpenProcess(0x0008 | 0x0010 | 0x0020, false, processId);
            if (hProcess == IntPtr.Zero)
                return;

            try {
                IntPtr pItem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf<LVITEM>(), 0x1000 | 0x2000, 0x04);
                IntPtr pText = VirtualAllocEx(hProcess, IntPtr.Zero, 520, 0x1000 | 0x2000, 0x04);

                try {
                    for (int attempt = 0; attempt < 40; attempt++) { // 40 попыток по 20 мс = 800 мс
                        await Task.Delay(20);

                        int count = (int)SendMessage(listView, 0x1004, IntPtr.Zero, IntPtr.Zero);
                        for (int i = 0; i < count; i++) {
                            var lvItem = new LVITEM {
                                mask = 0x0001,
                                iItem = i,
                                pszText = pText,
                                cchTextMax = 260
                            };

                            byte[] itemBytes = new byte[Marshal.SizeOf<LVITEM>()];
                            IntPtr ptr = Marshal.AllocHGlobal(itemBytes.Length);
                            Marshal.StructureToPtr(lvItem, ptr, false);
                            Marshal.Copy(ptr, itemBytes, 0, itemBytes.Length);
                            Marshal.FreeHGlobal(ptr);

                            WriteProcessMemory(hProcess, pItem, itemBytes, (uint)itemBytes.Length, out _);
                            SendMessage(listView, 0x1073, (IntPtr)i, pItem);

                            byte[] textBuffer = new byte[520];
                            ReadProcessMemory(hProcess, pText, textBuffer, 520, out _);
                            string itemText = System.Text.Encoding.Unicode.GetString(textBuffer).Split('\0')[0];

                            if (string.Equals(itemText, targetName, StringComparison.OrdinalIgnoreCase)) {
                                var pt = new POINT { X = screenX - 35, Y = screenY - 35 };
                                ScreenToClient(listView, ref pt);

                                byte[] ptBytes = new byte[8];
                                BitConverter.GetBytes(Math.Max(0, pt.X)).CopyTo(ptBytes, 0);
                                BitConverter.GetBytes(Math.Max(0, pt.Y)).CopyTo(ptBytes, 4);

                                WriteProcessMemory(hProcess, pText, ptBytes, 8, out _);
                                SendMessage(listView, 0x1031, (IntPtr)i, pText);
                                return;
                            }
                        }
                    }
                }
                finally {
                    VirtualFreeEx(hProcess, pItem, 0, 0x8000);
                    VirtualFreeEx(hProcess, pText, 0, 0x8000);
                }
            }
            finally {
                CloseHandle(hProcess);
            }
        }


        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        private static IntPtr FindDesktopListView() {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (defView == IntPtr.Zero) {
                EnumWindows((hwnd, lParam) =>
                {
                    IntPtr child = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (child != IntPtr.Zero) {
                        defView = child;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }

            return defView != IntPtr.Zero ? FindWindowEx(defView, IntPtr.Zero, "SysListView32", null) : IntPtr.Zero;
        }
    }
}
