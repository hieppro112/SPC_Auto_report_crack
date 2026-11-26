using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows.Threading;
using auto_scan_crack.Database;

namespace auto_scan_crack
{
    /// <summary>
    /// Đếm giờ cho PO: thời gian = GOODCNT × 7 giây + 10 giây bù (GOODCNT lấy từ MANUFA_F_PD_DT_ASSIST).
    /// Hết giờ → thông báo toàn màn hình nhắc người dùng, chỉ đóng khi bấm ĐÓNG.
    /// Trạng thái được lưu ra file để tắt/mở lại app vẫn tiếp tục đếm.
    /// </summary>
    public partial class MainWindow
    {
        private const int SecondsPerPiece = 7;

        /// <summary>Thời gian bù thêm cho mỗi PO (giây).</summary>
        private const int ExtraSeconds = 10;

        private static readonly string PoTimerFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "auto_scan_crack", "po_timer.json");

        private sealed record PoTimerState(string Po, int GoodCount, DateTime StartedAt, DateTime EndAt);

        private readonly DispatcherTimer _poTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private PoTimerState? _poTimerState;

        /// <summary>Gọi 1 lần khi mở app: khôi phục PO đang đếm dở (nếu có).</summary>
        private void InitPoTimer()
        {
            _poTimer.Tick += (_, _) => PoTimerTick();
            try
            {
                if (File.Exists(PoTimerFile))
                    _poTimerState = JsonSerializer.Deserialize<PoTimerState>(File.ReadAllText(PoTimerFile));
            }
            catch
            {
                _poTimerState = null;   // file hỏng → bỏ qua
            }

            if (_poTimerState is not null)
            {
                lblCurrentPO.Text = _poTimerState.Po;
                _poTimer.Start();
                PoTimerTick();   // nếu đã hết giờ trong lúc tắt app thì nhắc ngay
            }
        }

        /// <summary>
        /// Nhập PO vào WorkSupportSystem xong → lấy GOODCNT từ DB và bắt đầu đếm giờ (thay PO đang đếm trước đó).
        /// </summary>
        private async Task StartPoTimerAsync(string po, DateTime startedAt)
        {
            int? good;
            try
            {
                good = await PoRepository.GetGoodCountAsync(po);
            }
            catch (Exception ex)
            {
                ShowError("KHÔNG LẤY ĐƯỢC GOODCNT", $"PO: {po}\nKhông tính được thời gian cho PO.\n{ex.Message}");
                return;
            }

            if (good is null)
            {
                ShowError("KHÔNG TÌM THẤY PO TRONG DB",
                    $"PO {po} không có trong MANUFA_F_PD_DT_ASSIST nên không tính được thời gian.");
                return;
            }
            if (good <= 0)
            {
                ShowError("GOODCNT = 0", $"PO {po} có GOODCNT = {good} nên không đặt thời gian nhắc.");
                return;
            }

            var state = new PoTimerState(po, good.Value, startedAt,
                startedAt.AddSeconds(good.Value * SecondsPerPiece + ExtraSeconds));
            _poTimerState = state;
            SavePoTimer(state);
            UpdatePoFinishLock();   // chưa đủ giờ → khóa nút POFinish trên web
            _poTimer.Start();
            PoTimerTick();
        }

        private void PoTimerTick()
        {
            var state = _poTimerState;
            if (state is null)
            {
                _poTimer.Stop();
                return;
            }

            var remaining = state.EndAt - DateTime.Now;
            if (remaining > TimeSpan.Zero)
            {
                lblPoTimerCaption.Text = $"PO còn lại ({state.GoodCount} × {SecondsPerPiece}s + {ExtraSeconds}s)";
                lblPoRemaining.Text = FormatDuration(remaining);
                return;
            }

            // Hết giờ
            _poTimerState = null;
            _poTimer.Stop();
            SavePoTimer(null);
            UpdatePoFinishLock();   // đủ giờ → mở khóa nút POFinish
            lblPoTimerCaption.Text = "Thời gian PO";
            lblPoRemaining.Text = $"Đã đủ ({state.Po})";
            ShowPoReminder(state);
        }

        private void ShowPoReminder(PoTimerState state)
        {
            SystemSounds.Exclamation.Play();
            var total = TimeSpan.FromSeconds(state.GoodCount * SecondsPerPiece + ExtraSeconds);
            var reminder = new AlertWindow(
                "ĐÃ ĐỦ THỜI GIAN CHO PO",
                $"PO: {state.Po}\n" +
                $"GOODCNT {state.GoodCount} × {SecondsPerPiece} giây + {ExtraSeconds} giây bù = {FormatDuration(total)}\n" +
                $"Bắt đầu {state.StartedAt:HH:mm:ss dd/MM} — Kết thúc {state.EndAt:HH:mm:ss dd/MM}",
                autoCloseSeconds: 0,            // quan trọng: không tự đóng, phải bấm ĐÓNG
                backgroundColor: "#E65100");
            reminder.Show();
            SetStatus($"Đã đủ thời gian cho PO {state.Po}  ({DateTime.Now:HH:mm:ss})");
        }

        private static void SavePoTimer(PoTimerState? state)
        {
            try
            {
                if (state is null)
                {
                    if (File.Exists(PoTimerFile)) File.Delete(PoTimerFile);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(PoTimerFile)!);
                File.WriteAllText(PoTimerFile, JsonSerializer.Serialize(state));
            }
            catch
            {
                // không lưu được thì vẫn đếm bình thường trong lúc app đang mở
            }
        }

        private static string FormatDuration(TimeSpan t) =>
            $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }
}
