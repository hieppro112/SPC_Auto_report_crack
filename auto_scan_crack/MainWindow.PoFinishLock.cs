namespace auto_scan_crack
{
    /// <summary>
    /// Khóa nút POFinish (#btnPOFinish) trên web khi PO đang đếm giờ chưa đủ thời gian.
    /// Cơ chế: mỗi tab được chèn script khóa (WebAutomation.BuildPoFinishLockScript) kèm số PO và giờ kết thúc.
    /// Script được đăng ký chạy lại mỗi lần trang tải (postback/chuyển trang vẫn giữ khóa)
    /// và áp dụng ngay cho trang đang mở. Hết giờ thì script tự mở khóa; app cũng gỡ script khi PO hết giờ.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Gọi khi trạng thái đếm giờ PO thay đổi (bắt đầu / khôi phục / hết giờ): cập nhật khóa cho mọi tab.</summary>
        private void UpdatePoFinishLock()
        {
            foreach (var tab in _tabs)
                _ = ApplyPoFinishLockAsync(tab);
        }

        /// <summary>Đăng ký lại script khóa cho 1 tab theo PO đang đếm giờ (không có PO → bỏ khóa).</summary>
        private async Task ApplyPoFinishLockAsync(BrowserTab tab)
        {
            var core = tab.View.CoreWebView2;
            if (core is null) return;

            var state = _poTimerState;
            var script = state is null
                ? WebAutomation.BuildPoFinishLockScript(null, 0)
                : WebAutomation.BuildPoFinishLockScript(state.Po, new DateTimeOffset(state.EndAt).ToUnixTimeMilliseconds());

            try
            {
                if (tab.PoLockScriptId is { } oldId)
                {
                    core.RemoveScriptToExecuteOnDocumentCreated(oldId);
                    tab.PoLockScriptId = null;
                }
                if (state is not null)
                    tab.PoLockScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);

                // Áp dụng ngay cho trang đang mở (khóa hoặc mở khóa)
                await core.ExecuteScriptAsync(script);
            }
            catch
            {
                // tab đã đóng / trình duyệt chưa sẵn sàng → bỏ qua, lần tải trang sau sẽ áp dụng lại
            }
        }

        /// <summary>Trang web báo người dùng vừa bấm POFinish khi chưa đủ giờ.</summary>
        private void OnPoFinishBlocked(string po)
        {
            var state = _poTimerState;
            var left = state is not null && state.Po == po ? state.EndAt - DateTime.Now : TimeSpan.Zero;
            ShowError("CHƯA ĐỦ THỜI GIAN CHO PO",
                $"PO: {po}\nCòn {FormatDuration(left > TimeSpan.Zero ? left : TimeSpan.Zero)} nữa mới được nhấn POFinish.\n" +
                "Vui lòng chờ hết thời gian rồi nhấn lại.");
        }
    }
}
