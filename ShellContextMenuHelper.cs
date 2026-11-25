using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Papochka {
    public static class ShellContextMenuHelper {
        private const uint CMF_NORMAL = 0x00000000;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint CMD_FIRST = 1;
        private const uint CMD_LAST = 30000;
        private const uint CMD_REMOVE_FROM_FOLDER = 30001;

        private const uint MF_BYPOSITION = 0x00000400;
        private const uint MF_STRING = 0x00000000;
        private const uint GCS_VERBW = 0x00000004;

        private const uint CMF_EXTENDEDVERBS = 0x00000100;
        private const uint CMIC_MASK_SHIFT_DOWN = 0x10000000;

        public static bool ShowContextMenu(IntPtr ownerHwnd, string filePath, Point screenPosition) {
            if (!File.Exists(filePath) && !Directory.Exists(filePath))
                return false;

            IntPtr pidl = IntPtr.Zero;
            IntPtr hMenu = IntPtr.Zero;

            try {
                int hr = SHParseDisplayName(filePath, IntPtr.Zero, out pidl, 0, out _);
                if (hr != 0 || pidl == IntPtr.Zero)
                    return false;

                Guid iidShellFolder = typeof(IShellFolder).GUID;
                hr = SHBindToParent(pidl, ref iidShellFolder, out IntPtr pParentFolder, out IntPtr pidlChild);
                if (hr != 0 || pParentFolder == IntPtr.Zero)
                    return false;

                var parentFolder = (IShellFolder)Marshal.GetObjectForIUnknown(pParentFolder);

                Guid iidContextMenu = typeof(IContextMenu).GUID;
                IntPtr[] apidl = { pidlChild };
                hr = parentFolder.GetUIObjectOf(ownerHwnd, 1, apidl, ref iidContextMenu, IntPtr.Zero, out IntPtr pContextMenu);
                if (hr != 0 || pContextMenu == IntPtr.Zero)
                    return false;

                var contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(pContextMenu);

                hMenu = CreatePopupMenu();
                bool isShiftDown = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
                uint flags = CMF_NORMAL | (isShiftDown ? CMF_EXTENDEDVERBS : 0);

                contextMenu.QueryContextMenu(hMenu, 0, CMD_FIRST, CMD_LAST, flags);


                // Ищем позицию системного пункта "delete", чтобы вставить свой пункт рядом с ним
                uint insertPos = FindDeleteItemPosition(hMenu, contextMenu);
                InsertMenu(hMenu, insertPos, MF_BYPOSITION | MF_STRING, (UIntPtr)CMD_REMOVE_FROM_FOLDER, "Убрать из Папочки");

                SetForegroundWindow(ownerHwnd);

                uint selectedCmd = TrackPopupMenuEx(
                    hMenu,
                    TPM_RETURNCMD | TPM_RIGHTBUTTON,
                    (int)screenPosition.X,
                    (int)screenPosition.Y,
                    ownerHwnd,
                    IntPtr.Zero);

                if (selectedCmd == CMD_REMOVE_FROM_FOLDER) {
                    return true;
                }

                if (selectedCmd >= CMD_FIRST && selectedCmd <= CMD_LAST) {
                    bool shiftOnClick = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;

                    var invokeInfo = new CMINVOKECOMMANDINFO {
                        cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                        fMask = shiftOnClick ? CMIC_MASK_SHIFT_DOWN : 0,
                        hwnd = IntPtr.Zero,
                        lpVerb = (IntPtr)(selectedCmd - CMD_FIRST),
                        nShow = 1
                    };

                    DesktopHelper.IsSystemDialogOpening = true;
                    SetWindowPos(ownerHwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002); // HWND_TOPMOST
                    SetWindowPos(ownerHwnd, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002); // HWND_NOTOPMOST
                    SetForegroundWindow(ownerHwnd);

                    contextMenu.InvokeCommand(ref invokeInfo);

                    _ = System.Threading.Tasks.Task.Delay(300).ContinueWith(_ =>
                    {
                        DesktopHelper.IsSystemDialogOpening = false;
                        SetWindowPos(ownerHwnd, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                    });

                }


                Marshal.ReleaseComObject(contextMenu);
                Marshal.ReleaseComObject(parentFolder);
            }
            finally {
                if (hMenu != IntPtr.Zero)
                    DestroyMenu(hMenu);
                if (pidl != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pidl);
            }

            return false;
        }

        private static uint FindDeleteItemPosition(IntPtr hMenu, IContextMenu contextMenu) {
            int count = GetMenuItemCount(hMenu);
            IntPtr buffer = Marshal.AllocHGlobal(256);
            try {
                for (uint i = 0; i < count; i++) {
                    uint id = GetMenuItemID(hMenu, (int)i);
                    if (id >= CMD_FIRST && id <= CMD_LAST) {
                        int hr = contextMenu.GetCommandString((UIntPtr)(id - CMD_FIRST), GCS_VERBW, IntPtr.Zero, buffer, 128);
                        if (hr == 0) {
                            string? verb = Marshal.PtrToStringUni(buffer);
                            if (string.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase)) {
                                return i; // Вставим прямо перед системным "Удалить" (или i + 1, если нужно после него)
                            }
                        }
                    }
                }
            }
            finally {
                Marshal.FreeHGlobal(buffer);
            }

            return uint.MaxValue; // Если "delete" не найден — добавляем в самый конец меню
        }

        public static int ShowMiniFolderMenu(IntPtr ownerHwnd, Point screenPosition) {
            IntPtr hMenu = CreatePopupMenu();
            try {
                InsertMenu(hMenu, 0, MF_BYPOSITION | MF_STRING, (UIntPtr)1, "Настроить эту Папочку");
                InsertMenu(hMenu, 1, MF_BYPOSITION | MF_STRING, (UIntPtr)2, "Удалить эту Папочку");
                SetForegroundWindow(ownerHwnd);
                uint cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, (int)screenPosition.X, (int)screenPosition.Y, ownerHwnd, IntPtr.Zero);
                return (int)cmd;
            }
            finally {
                DestroyMenu(hMenu);
            }
        }

        public static void ShowBackgroundContextMenu(IntPtr ownerHwnd, string targetFolderPath, Point screenPosition, Action<string>? onNewFileCreated = null) {
            IntPtr pidl = IntPtr.Zero;
            IntPtr hMenu = IntPtr.Zero;
            IContextMenu2? contextMenu2 = null;

            // Следим за появлением новых файлов в целевой папке во время работы меню
            using var watcher = new FileSystemWatcher(targetFolderPath);
            watcher.Created += (s, e) => onNewFileCreated?.Invoke(e.FullPath);
            watcher.EnableRaisingEvents = true;

            // Хук для корректной отрисовки подменю "Создать" (WM_INITMENUPOPUP)
            System.Windows.Interop.HwndSourceHook hook = (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                const int WM_INITMENUPOPUP = 0x0117;
                const int WM_DRAWITEM = 0x002B;
                const int WM_MEASUREITEM = 0x002C;

                if (contextMenu2 != null && (msg == WM_INITMENUPOPUP || msg == WM_DRAWITEM || msg == WM_MEASUREITEM)) {
                    contextMenu2.HandleMenuMsg((uint)msg, wParam, lParam);
                    handled = true;
                }
                return IntPtr.Zero;
            };

            var hwndSource = System.Windows.Interop.HwndSource.FromHwnd(ownerHwnd);
            hwndSource?.AddHook(hook);

            try {
                int hr = SHGetDesktopFolder(out IShellFolder desktopFolder);
                if (hr != 0 || desktopFolder == null)
                    return;

                uint pchEaten = 0, pdwAttributes = 0;
                desktopFolder.ParseDisplayName(ownerHwnd, IntPtr.Zero, targetFolderPath, ref pchEaten, out pidl, ref pdwAttributes);
                if (pidl == IntPtr.Zero)
                    return;

                Guid iidShellFolder = typeof(IShellFolder).GUID;
                desktopFolder.BindToObject(pidl, IntPtr.Zero, ref iidShellFolder, out IntPtr pTargetFolder);
                if (pTargetFolder == IntPtr.Zero)
                    return;

                var targetFolder = (IShellFolder)Marshal.GetObjectForIUnknown(pTargetFolder);

                // Запрашиваем именно ФОНОВОЕ контекстное меню через CreateViewObject
                Guid iidContextMenu = typeof(IContextMenu).GUID;
                targetFolder.CreateViewObject(ownerHwnd, ref iidContextMenu, out IntPtr pContextMenu);
                if (pContextMenu == IntPtr.Zero)
                    return;

                var contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(pContextMenu);
                contextMenu2 = contextMenu as IContextMenu2;

                hMenu = CreatePopupMenu();
                contextMenu.QueryContextMenu(hMenu, 0, CMD_FIRST, CMD_LAST, CMF_NORMAL);

                SetForegroundWindow(ownerHwnd);

                uint selectedCmd = TrackPopupMenuEx(
                    hMenu,
                    TPM_RETURNCMD | TPM_RIGHTBUTTON,
                    (int)screenPosition.X,
                    (int)screenPosition.Y,
                    ownerHwnd,
                    IntPtr.Zero);

                if (selectedCmd >= CMD_FIRST && selectedCmd <= CMD_LAST) {
                    var invokeInfo = new CMINVOKECOMMANDINFO {
                        cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                        hwnd = ownerHwnd,
                        lpVerb = (IntPtr)(selectedCmd - CMD_FIRST),
                        nShow = 1
                    };
                    contextMenu.InvokeCommand(ref invokeInfo);
                }

                Marshal.ReleaseComObject(contextMenu);
                Marshal.ReleaseComObject(targetFolder);
                Marshal.ReleaseComObject(desktopFolder);
            }
            finally {
                hwndSource?.RemoveHook(hook);
                if (hMenu != IntPtr.Zero)
                    DestroyMenu(hMenu);
                if (pidl != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pidl);
            }
        }

        #region COM & Win32 API

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E6-0000-0000-C000-000000000046")]
        private interface IShellFolder {
            void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
            void EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);
            void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
            [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
            void CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
            void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
            [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
            void GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);
            void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E4-0000-0000-C000-000000000046")]
        private interface IContextMenu {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F4-0000-0000-C000-000000000046")]
        private interface IContextMenu2 {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
            [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct CMINVOKECOMMANDINFO {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            public string lpParameters;
            public string lpDirectory;
            public int nShow;
            public uint dwHotKey;
            public IntPtr hIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll")]
        private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetMenuItemCount(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern uint GetMenuItemID(IntPtr hMenu, int nPos);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool InsertMenu(IntPtr hMenu, uint uPosition, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

        [DllImport("shell32.dll")]
        private static extern int SHGetDesktopFolder(out IShellFolder ppshf);

        [DllImport("user32.dll")] 
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        #endregion
    }
}
