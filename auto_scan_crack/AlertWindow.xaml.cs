using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace auto_scan_crack
{
    /// <summary>
    /// Cảnh báo toàn màn hình.
    /// - autoCloseSeconds &gt; 0: đóng khi bấm ĐÓNG / Esc / Enter, hoặc tự đóng sau số giây đó.
    /// - autoCloseSeconds &lt;= 0 (thông báo quan trọng): KHÔNG tự đóng, chỉ đóng khi người dùng bấm chuột vào nút ĐÓNG
    ///   (bỏ qua phím để Enter của máy quét không vô tình đóng mất).
    /// </summary>
    public partial class AlertWindow : Window
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly bool _requireClick;
        private int _secondsLeft;

        public AlertWindow(string title, string message, int autoCloseSeconds = 5, string? backgroundColor = null)
        {
            InitializeComponent();
            lblTitle.Text = title;
            lblMessage.Text = message;

            if (backgroundColor is not null)
            {
                var brush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(backgroundColor)!;
                Background = brush;
                btnClose.Foreground = brush;
            }

            _requireClick = autoCloseSeconds <= 0;
            _secondsLeft = autoCloseSeconds;

            if (_requireClick)
            {
                btnClose.Focusable = false;   // nút không giữ focus → Enter/Space không bấm được
                lblCountdown.Text = "Bấm ĐÓNG để xác nhận";
            }
            else
            {
                UpdateCountdown();
                _timer.Tick += (_, _) =>
                {
                    _secondsLeft--;
                    if (_secondsLeft <= 0) Close();
                    else UpdateCountdown();
                };
            }

            Loaded += (_, _) =>
            {
                if (!_requireClick)
                {
                    _timer.Start();
                    btnClose.Focus();
                }
                Activate();
            };
            Closed += (_, _) => _timer.Stop();
        }

        private void UpdateCountdown() => lblCountdown.Text = $"Tự đóng sau {_secondsLeft} giây";

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (_requireClick)
            {
                e.Handled = true;   // thông báo quan trọng: bỏ qua mọi phím
                return;
            }
            if (e.Key is Key.Escape or Key.Enter)
            {
                e.Handled = true;
                Close();
            }
        }
    }
}
