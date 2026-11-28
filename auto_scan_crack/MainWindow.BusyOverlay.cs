namespace auto_scan_crack
{
    /// <summary>Lớp phủ "đang tự động thao tác trên web" trong lúc chạy PO (xem BusyOverlayWindow).</summary>
    public partial class MainWindow
    {
        private BusyOverlayWindow? _busyOverlay;

        private void ShowBusyOverlay(string target)
        {
            HideBusyOverlay();
            var overlay = new BusyOverlayWindow(target) { Owner = this };   // luôn nằm trên cửa sổ chính, thu nhỏ theo
            overlay.SetDetail(lblStatus.Text);
            _busyOverlay = overlay;

            LocationChanged += MainWindow_LayoutChangedForOverlay;
            SizeChanged += MainWindow_LayoutChangedForOverlay;
            StateChanged += MainWindow_LayoutChangedForOverlay;
            browserHost.SizeChanged += MainWindow_LayoutChangedForOverlay;

            overlay.CoverElement(browserHost);
            overlay.Show();
        }

        private void HideBusyOverlay()
        {
            if (_busyOverlay is null) return;
            LocationChanged -= MainWindow_LayoutChangedForOverlay;
            SizeChanged -= MainWindow_LayoutChangedForOverlay;
            StateChanged -= MainWindow_LayoutChangedForOverlay;
            browserHost.SizeChanged -= MainWindow_LayoutChangedForOverlay;
            _busyOverlay.Close();
            _busyOverlay = null;
        }

        private void MainWindow_LayoutChangedForOverlay(object? sender, EventArgs e)
        {
            // Đợi layout xong rồi mới đo lại vị trí vùng web
            Dispatcher.BeginInvoke(() => _busyOverlay?.CoverElement(browserHost),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
