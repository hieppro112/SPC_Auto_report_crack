using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Threading;

namespace auto_scan_crack
{
    /// <summary>
    /// Luân phiên nhân viên: mỗi MSNV chỉ có hiệu lực 2 tiếng kể từ lúc quét.
    /// Hết 2 tiếng → thông báo toàn màn hình, không cho quét PO cho đến khi quét MSNV KHÁC MSNV hiện tại.
    /// Vd: 8h quét 112 → 10h phải quét người khác (113) → 12h quét lại 112 được.
    /// - Quét lại đúng MSNV đang dùng trước khi hết giờ: vẫn nhận nhưng KHÔNG tính lại 2 tiếng (tránh lách luật).
    /// - Quét MSNV khác bất cứ lúc nào: đổi người và bắt đầu tính 2 tiếng mới.
    /// Giờ quét được lưu ra file để tắt/mở lại app vẫn tính tiếp.
    /// </summary>
    public partial class MainWindow
    {
        private static readonly TimeSpan MsnvValidity = TimeSpan.FromHours(2);

        private static readonly string MsnvStateFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "auto_scan_crack", "msnv_state.json");

        private sealed record MsnvState(string Msnv, DateTime ScannedAt);

        private readonly DispatcherTimer _msnvTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private MsnvState? _msnvState;
        private bool _msnvExpiredNotified;

        // Ô MSNV nền tối chữ vàng; hết giờ → nền đỏ đậm chữ trắng cho nổi bật
        private static readonly Brush MsnvExpiredBrush = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
        private static readonly Brush MsnvNormalForeground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));

        private void SetMsnvExpiredLook(bool expired)
        {
            txtMsnv.Background = expired ? MsnvExpiredBrush : Brushes.Transparent;
            txtMsnv.Foreground = expired ? Brushes.White : MsnvNormalForeground;
        }

        private bool IsMsnvExpired => _msnvState is { } s && DateTime.Now - s.ScannedAt >= MsnvValidity;

        /// <summary>Gọi 1 lần khi mở app (sau khi đã nạp MSNV vào ô): khôi phục giờ quét MSNV.</summary>
        private void InitMsnvRotation()
        {
            try
            {
                if (File.Exists(MsnvStateFile))
                    _msnvState = JsonSerializer.Deserialize<MsnvState>(File.ReadAllText(MsnvStateFile));
            }
            catch
            {
                _msnvState = null;   // file hỏng → coi như vừa quét
            }

            var msnv = txtMsnv.Text.Trim();
            if (msnv.Length == 0)
                _msnvState = null;
            else if (_msnvState is null || _msnvState.Msnv != msnv)
            {
                // Bản cũ chưa lưu giờ quét → tính từ lúc mở app
                _msnvState = new MsnvState(msnv, DateTime.Now);
                SaveMsnvState();
            }

            _msnvTimer.Tick += (_, _) => MsnvTimerTick();
            _msnvTimer.Start();
            MsnvTimerTick();   // đã quá 2 tiếng trong lúc tắt app → nhắc ngay
        }

        /// <summary>Quét được 1 MSNV (4–5 chữ số). Hết giờ mà quét lại đúng người cũ thì báo lỗi, không nhận.</summary>
        private void OnMsnvScanned(string code)
        {
            var current = _msnvState;

            if (current is not null && code == current.Msnv)
            {
                if (IsMsnvExpired)
                {
                    ShowError("PHẢI ĐỔI NHÂN VIÊN",
                        $"MSNV {code} đã làm đủ {MsnvValidity.TotalHours:0} tiếng (từ {current.ScannedAt:HH:mm}).\n" +
                        $"Vui lòng quét mã nhân viên KHÁC (không phải {code}).", txtPO);
                    return;
                }
                // Cùng người, chưa hết giờ → giữ nguyên giờ bắt đầu
                SetStatus($"MSNV {code} vẫn đang dùng — còn {FormatDuration(current.ScannedAt + MsnvValidity - DateTime.Now)} đến lượt đổi người.");
                txtPO.Focus();
                return;
            }

            txtMsnv.Text = code;
            SaveText(MsnvFile, code, "MSNV");
            _msnvState = new MsnvState(code, DateTime.Now);
            _msnvExpiredNotified = false;
            SaveMsnvState();
            MsnvTimerTick();
            SetStatus($"Đã quét MSNV: {code}  ({DateTime.Now:HH:mm:ss}) — đổi người lúc {_msnvState.ScannedAt + MsnvValidity:HH:mm}");
            txtPO.Focus();
        }

        private void MsnvTimerTick()
        {
            var state = _msnvState;
            if (state is null)
            {
                lblMsnvRemaining.Text = "—";
                SetMsnvExpiredLook(false);
                return;
            }

            var remaining = state.ScannedAt + MsnvValidity - DateTime.Now;
            if (remaining > TimeSpan.Zero)
            {
                lblMsnvCaption.Text = $"Đổi NV lúc {state.ScannedAt + MsnvValidity:HH:mm}";
                lblMsnvRemaining.Text = FormatDuration(remaining);
                SetMsnvExpiredLook(false);
                return;
            }

            lblMsnvCaption.Text = "Đổi nhân viên";
            lblMsnvRemaining.Text = "ĐẾN GIỜ ĐỔI";
            SetMsnvExpiredLook(true);

            if (!_msnvExpiredNotified)
            {
                _msnvExpiredNotified = true;
                ShowMsnvRotationReminder(state);
            }
        }

        private void ShowMsnvRotationReminder(MsnvState state)
        {
            SystemSounds.Exclamation.Play();
            var reminder = new AlertWindow(
                "ĐẾN GIỜ ĐỔI NHÂN VIÊN",
                $"MSNV {state.Msnv} đã làm đủ {MsnvValidity.TotalHours:0} tiếng (từ {state.ScannedAt:HH:mm}).\n" +
                $"Vui lòng quét mã nhân viên KHÁC (không phải {state.Msnv}).\n" +
                "Chưa đổi nhân viên thì không quét được PO.",
                autoCloseSeconds: 0,            // không tự đóng, phải bấm ĐÓNG
                backgroundColor: "#6A1B9A");
            reminder.Closed += (_, _) =>
            {
                Activate();
                txtPO.Focus();
            };
            reminder.Show();
            SetStatus($"Đến giờ đổi nhân viên: quét MSNV khác {state.Msnv}  ({DateTime.Now:HH:mm:ss})");
        }

        private void SaveMsnvState()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MsnvStateFile)!);
                File.WriteAllText(MsnvStateFile, JsonSerializer.Serialize(_msnvState));
            }
            catch
            {
                // không lưu được thì vẫn tính giờ bình thường trong lúc app đang mở
            }
        }
    }
}
