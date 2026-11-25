using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace auto_scan_crack
{
    /// <summary>
    /// Cảnh báo toàn màn hình: đóng khi người dùng bấm ĐÓNG / Esc / Enter, hoặc tự đóng sau vài giây.
    /// </summary>
    public partial class AlertWindow : Window
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private int _secondsLeft;

        public AlertWindow(string title, string message, int autoCloseSeconds = 5)
        {
            InitializeComponent();
            lblTitle.Text = title;
            lblMessage.Text = message;

            _secondsLeft = autoCloseSeconds;
            UpdateCountdown();
            _timer.Tick += (_, _) =>
            {
                _secondsLeft--;
                if (_secondsLeft <= 0) Close();
                else UpdateCountdown();
            };

            Loaded += (_, _) =>
            {
                _timer.Start();
                Activate();
                btnClose.Focus();
            };
            Closed += (_, _) => _timer.Stop();
        }

        private void UpdateCountdown() => lblCountdown.Text = $"Tự đóng sau {_secondsLeft} giây";

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Escape or Key.Enter)
            {
                e.Handled = true;
                Close();
            }
        }
    }
}
