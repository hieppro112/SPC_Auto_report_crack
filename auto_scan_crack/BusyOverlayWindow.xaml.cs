using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace auto_scan_crack
{
    /// <summary>
    /// Cửa sổ phủ lên vùng web trong lúc app đang tự động thao tác:
    /// hiện thông báo "đang thao tác" và chặn chuột để người dùng không bấm thêm vào web.
    /// Không bao giờ nhận focus/kích hoạt (WS_EX_NOACTIVATE) nên không cướp bàn phím của trang web
    /// hay của máy quét; app tự động thao tác qua DevTools nên không bị lớp phủ chặn.
    /// </summary>
    public partial class BusyOverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DateTime _startedAt = DateTime.Now;

        public BusyOverlayWindow(string target)
        {
            InitializeComponent();
            lblTarget.Text = target;
            UpdateElapsed();
            _timer.Tick += (_, _) => UpdateElapsed();
            Loaded += (_, _) => _timer.Start();
            Closed += (_, _) => _timer.Stop();
            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };
        }

        /// <summary>Bước đang làm (lấy từ thanh trạng thái).</summary>
        public void SetDetail(string text) => lblDetail.Text = text;

        /// <summary>Đặt lớp phủ trùng đúng vùng <paramref name="area"/> (vùng web trên cửa sổ chính).</summary>
        public void CoverElement(FrameworkElement area)
        {
            if (!area.IsVisible || PresentationSource.FromVisual(area) is not { CompositionTarget: { } target })
                return;
            var fromDevice = target.TransformFromDevice;
            var topLeft = fromDevice.Transform(area.PointToScreen(new Point(0, 0)));
            var bottomRight = fromDevice.Transform(area.PointToScreen(new Point(area.ActualWidth, area.ActualHeight)));
            Left = topLeft.X;
            Top = topLeft.Y;
            Width = Math.Max(0, bottomRight.X - topLeft.X);
            Height = Math.Max(0, bottomRight.Y - topLeft.Y);
        }

        private void UpdateElapsed()
        {
            var t = DateTime.Now - _startedAt;
            lblElapsed.Text = $"Đã chạy {(int)t.TotalMinutes:00}:{t.Seconds:00}";
        }
    }
}
