using System.Diagnostics;
using System.Runtime.InteropServices;

namespace auto_scan_crack
{
    /// <summary>
    /// Điều khiển app WorkSupportSystem.exe (加工支援システム): tìm tiến trình, đưa cửa sổ lên trước
    /// và gõ phím như máy quét barcode (bàn phím thật qua SendInput).
    /// </summary>
    internal static class WorkSupportApp
    {
        /// <summary>Tên tiến trình (cột Name trong Task Manager &gt; Details, bỏ ".exe").</summary>
        public const string ProcessName = "WorkSupportSystem";

        /// <summary>Tiến trình WorkSupportSystem đang chạy; null nếu chưa mở.</summary>
        public static Process? FindRunning() => Process.GetProcessesByName(ProcessName).FirstOrDefault();

        /// <summary>Cửa sổ chính của tiến trình (kể cả khi đang thu nhỏ); IntPtr.Zero nếu không có.</summary>
        public static IntPtr FindMainWindow(Process process)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
                return process.MainWindowHandle;

            // MainWindowHandle có thể = 0 khi cửa sổ đang thu nhỏ → tự duyệt các cửa sổ cấp cao của tiến trình
            IntPtr found = IntPtr.Zero;
            uint pid = (uint)process.Id;
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out uint wpid);
                if (wpid == pid && IsWindowVisible(h) && GetWindow(h, GW_OWNER) == IntPtr.Zero)
                {
                    found = h;
                    return false;   // dừng duyệt
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Đưa cửa sổ lên trước; trả về true nếu cửa sổ đang nhận bàn phím thuộc đúng tiến trình đó.</summary>
        public static bool BringToFront(IntPtr hwnd, int processId)
        {
            if (IsIconic(hwnd))
                ShowWindow(hwnd, SW_RESTORE);

            if (!SetForegroundWindow(hwnd))
            {
                // Windows có thể chặn đổi cửa sổ trước → gắn tạm luồng nhập liệu với cửa sổ đang trước rồi thử lại
                uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                uint myThread = GetCurrentThreadId();
                AttachThreadInput(myThread, fgThread, true);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                AttachThreadInput(myThread, fgThread, false);
            }

            GetWindowThreadProcessId(GetForegroundWindow(), out uint fgPid);
            return fgPid == (uint)processId;
        }

        /// <summary>Gõ chuỗi như bàn phím thật (phím ảo + Shift khi cần), mỗi ký tự cách nhau vài ms.</summary>
        public static async Task TypeTextAsync(string text)
        {
            bool capsOn = (GetKeyState(VK_CAPITAL) & 1) != 0;
            foreach (char c in text)
            {
                short scan = VkKeyScanW(c);
                if (scan == -1) continue;   // ký tự không gõ được trên bàn phím hiện tại

                ushort vk = (ushort)(scan & 0xFF);
                bool shift = (scan & 0x100) != 0;
                if (capsOn && char.IsLetter(c)) shift = !shift;   // CapsLock đang bật thì đảo Shift để giữ đúng hoa/thường

                var inputs = new List<INPUT>();
                if (shift) inputs.Add(Key(VK_SHIFT, false));
                inputs.Add(Key(vk, false));
                inputs.Add(Key(vk, true));
                if (shift) inputs.Add(Key(VK_SHIFT, true));
                Send(inputs);

                await Task.Delay(20);
            }
        }

        public static void PressEnter() => Send([Key(VK_RETURN, false), Key(VK_RETURN, true)]);

        // ====== Win32 ======

        private static INPUT Key(ushort vk, bool up) => new()
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC),
                    dwFlags = up ? KEYEVENTF_KEYUP : 0,
                }
            }
        };

        private static void Send(List<INPUT> inputs) =>
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MAPVK_VK_TO_VSC = 0;
        private const ushort VK_SHIFT = 0x10;
        private const ushort VK_RETURN = 0x0D;
        private const int VK_CAPITAL = 0x14;
        private const int SW_RESTORE = 9;
        private const uint GW_OWNER = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;   // để struct đúng kích thước
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")] private static extern short VkKeyScanW(char ch);
        [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);
        [DllImport("user32.dll")] private static extern short GetKeyState(int nVirtKey);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }
}
