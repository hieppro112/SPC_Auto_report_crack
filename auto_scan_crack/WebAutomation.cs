using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace auto_scan_crack
{
    /// <summary>
    /// Thao tác tự động trên trang web (gõ phím / click chuột thật qua DevTools Protocol,
    /// nên trang ASP.NET nhận như người dùng thao tác).
    /// </summary>
    public class WebAutomation
    {
        // ====== Cấu hình selector — chỉnh tại đây nếu trang web thay đổi ======

        /// <summary>Label được click trước khi nhấn phím xóa.</summary>
        public const string MyVersionSelector = "#MyVersion";

        /// <summary>Ô nhập TerminalID.</summary>
        public const string TerminalInputSelector = "#txtTerminalID";

        /// <summary>Nút ▼ mở combobox Shift.</summary>
        public const string ShiftMenuSelector = "#ddlShiftCD_lblMenuTop";

        /// <summary>Chữ hiển thị giá trị Shift đang chọn.</summary>
        public const string ShiftSelectedSelector = "#ddlShiftCD_lblMenuSelect";

        /// <summary>Ô AttendanceDate (server tự điền sau khi chọn Shift).</summary>
        public const string DutyDateSelector = "#txtDutyYmd";

        /// <summary>Nút Login.</summary>
        public const string LoginButtonSelector = "#btnLogin";

        /// <summary>Tiêu đề trang sau khi Login.</summary>
        public const string TitleSelector = "#lblTitle";

        /// <summary>Chữ cần có trong tiêu đề để biết đã sang trang nhập nhân viên.</summary>
        public const string OperatorEntryTitle = "Operator entry";

        /// <summary>Tiêu đề trang Login (làm đầy đủ các bước).</summary>
        public const string LoginTitle = "Login";

        /// <summary>Tiêu đề trang PO entry (đã đăng nhập → chỉ làm từ bước nhập MSNV).</summary>
        public const string PoEntryTitle = "PO entry";

        /// <summary>
        /// Nhãn số PO (InstrNo) hiện ra sau khi nhập PO thành công.
        /// Chỉ khi nhãn này có trên trang mới chuyển sang mở WorkSupportSystem.
        /// </summary>
        public const string InstructionNoSelector = "#lblInstructionNo";

        /// <summary>Ô nhập MSNV trên trang Operator entry.</summary>
        public const string OperatorInputSelector = "#txtOperatorID";

        /// <summary>Nhãn PO hiện ra sau khi nhập MSNV thành công.</summary>
        public const string PoLabelSelector = "#lblPO";

        /// <summary>Tiền tố id của combobox Shift (các ô ẩn: _SelVal, _hidSelKeys, _hidSelItems, _btnListSelect).</summary>
        public const string ShiftPrefix = "ddlShiftCD";

        /// <summary>Các dòng trong danh sách Shift (id dạng ddlShiftCD_ListViewMenuDtl_ListViewItem_N).</summary>
        public const string ShiftListSelector =
            "li[id^='ddlShiftCD_ListViewMenuDtl'], #ddlShiftCD_updDDLUserControl .dropmenudtl li";

        /// <summary>
        /// JS xác định 1 dòng &lt;li&gt; có phải dòng cần chọn không (biến `want` = "SF1"):
        /// khớp nếu mã ẩn bằng SF1, hoặc chữ hiển thị bằng SF1 / bắt đầu bằng SF1 (vd "SF1 (06:00-14:00)").
        /// </summary>
        private const string ShiftItemMatchJs = """
            const isItem = li => {
              const W = want.toUpperCase();
              const norm = s => (s || '').replace(/[\s ]+/g, ' ').trim().toUpperCase();
              const k = li.querySelector('input[type=hidden]');
              const t = norm(li.textContent);
              if (k && norm(k.value) === W) return true;
              return t === W || (t.startsWith(W) && !/[A-Z0-9]/.test(t.charAt(W.length)));
            };
            """;

        /// <summary>
        /// Script chèn vào mọi trang: bắt chuỗi do máy quét gõ (gõ rất nhanh, kết thúc bằng Enter)
        /// khi con trỏ đang nằm trong trang web. Chỉ bắt 3 loại mã app hiểu (xem ScanClassifier):
        /// mã máy "TRTB…", MSNV 4–5 chữ số, PO 12 chữ số → báo về app. Mã khác để trang tự xử lý.
        /// </summary>
        public static readonly string ScanHookScript = """
            (() => {
              if (window.__autoScanHook) return;
              window.__autoScanHook = true;
              const MAX_GAP_MS = 50;   // máy quét gõ < 50ms/ký tự, người gõ tay chậm hơn nhiều
              const KNOWN = /^(TRTB.+|\d{4,5}|\d{12})$/i;
              let buf = '', last = 0;
              window.addEventListener('keydown', e => {
                if (window.__autoScanPaused) { buf = ''; return; }   // app đang tự gõ → bỏ qua
                const now = performance.now();
                if (now - last > MAX_GAP_MS) buf = '';
                last = now;
                if (e.key === 'Enter') {
                  if (buf.length > 1 && KNOWN.test(buf)) {
                    e.preventDefault();               // không để Enter của máy quét submit form
                    e.stopImmediatePropagation();
                    window.chrome.webview.postMessage(JSON.stringify({ type: 'scan', code: buf }));
                  }
                  buf = '';
                  return;
                }
                if (e.key && e.key.length === 1) buf += e.key;
              }, true);
            })();
            """;

        /// <summary>Nút kết thúc PO — bị khóa khi PO chưa đủ thời gian.</summary>
        public const string PoFinishButtonSelector = "#btnPOFinish";

        /// <summary>
        /// Script khóa nút POFinish khi PO chưa đủ thời gian.
        /// - Chỉ khóa nếu trang đang hiện đúng số PO đang đếm giờ (trang của PO khác vẫn bấm bình thường).
        /// - Chặn click / chạm / Enter vào nút ngay từ pha capture nên sự kiện của trang (postback) không chạy.
        /// - Làm mờ nút và hiện thời gian còn lại đè lên nút; hết giờ thì tự mở khóa (không cần app báo).
        /// - Bấm vào lúc đang khóa → báo về app ({type:'poFinishBlocked'}) để hiện cảnh báo.
        /// Không đổi value/disabled của nút để không ảnh hưởng dữ liệu postback của ASP.NET.
        /// <paramref name="po"/> = null → bỏ khóa.
        /// </summary>
        public static string BuildPoFinishLockScript(string? po, long endAtUnixMs)
        {
            var lockJson = po is null ? "null" : JsonSerializer.Serialize(new { po, endAt = endAtUnixMs });
            return $$"""
                (() => {
                  window.__poFinishLock = {{lockJson}};
                  if (window.__poFinishLockHooked) { window.__poFinishLockApply && window.__poFinishLockApply(); return; }
                  window.__poFinishLockHooked = true;
                  const SEL = {{Js(PoFinishButtonSelector)}};
                  const active = () => {
                    const L = window.__poFinishLock;
                    if (!L || Date.now() >= L.endAt || !document.body) return null;
                    return document.body.innerText.includes(L.po) ? L : null;
                  };
                  const fmt = ms => {
                    const s = Math.max(0, Math.ceil(ms / 1000));
                    const p = n => String(n).padStart(2, '0');
                    return (s >= 3600 ? p(Math.floor(s / 3600)) + ':' : '') + p(Math.floor(s % 3600 / 60)) + ':' + p(s % 60);
                  };
                  const onButton = el => !!(el && el.closest && el.closest(SEL));
                  let lastReport = 0;
                  const block = e => {
                    if (!onButton(e.target)) return;
                    const L = active();
                    if (!L) return;
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    if ((e.type === 'click' || e.type === 'touchend') && Date.now() - lastReport > 800) {
                      lastReport = Date.now();
                      window.chrome.webview.postMessage(JSON.stringify({ type: 'poFinishBlocked', po: L.po }));
                    }
                  };
                  for (const t of ['pointerdown', 'pointerup', 'mousedown', 'mouseup', 'click', 'dblclick', 'touchstart', 'touchend'])
                    window.addEventListener(t, block, { capture: true, passive: false });
                  window.addEventListener('keydown', e => {
                    if ((e.key === 'Enter' || e.key === ' ') && onButton(document.activeElement) && active()) {
                      e.preventDefault();
                      e.stopImmediatePropagation();
                    }
                  }, true);

                  // Hiện trạng thái khóa: làm mờ nút + nhãn đếm ngược đè lên nút (không nhận chuột)
                  let badge = null;
                  const apply = () => {
                    const b = document.querySelector(SEL);
                    const L = b ? active() : null;
                    if (!L) {
                      if (b && b.dataset.poLocked) { b.style.opacity = ''; b.style.cursor = ''; b.title = ''; delete b.dataset.poLocked; }
                      if (badge) badge.style.display = 'none';
                      return;
                    }
                    const left = fmt(L.endAt - Date.now());
                    b.dataset.poLocked = '1';
                    b.style.opacity = '0.35';
                    b.style.cursor = 'not-allowed';
                    b.title = 'Chưa đủ thời gian cho PO ' + L.po + ' — còn ' + left;
                    if (!badge || !badge.isConnected) {
                      badge = document.createElement('div');
                      badge.style.cssText = 'position:fixed;z-index:2147483647;pointer-events:none;display:flex;' +
                        'align-items:center;justify-content:center;flex-direction:column;font:bold 14px Segoe UI,Arial;' +
                        'color:#fff;background:rgba(183,28,28,.85);border-radius:6px;text-align:center;line-height:1.15';
                      document.body.appendChild(badge);
                    }
                    const r = b.getBoundingClientRect();
                    badge.style.left = r.left + 'px'; badge.style.top = r.top + 'px';
                    badge.style.width = r.width + 'px'; badge.style.height = r.height + 'px';
                    badge.style.display = (r.width && r.height) ? 'flex' : 'none';
                    badge.innerHTML = '<span style="font-size:11px">CHƯA ĐỦ GIỜ</span><span>' + left + '</span>';
                  };
                  window.__poFinishLockApply = apply;
                  setInterval(apply, 500);
                  apply();
                })();
                """;
        }

        private readonly CoreWebView2 _core;
        private readonly Func<bool> _isNavigating;

        public WebAutomation(CoreWebView2 core, Func<bool> isNavigating)
        {
            _core = core;
            _isNavigating = isNavigating;
        }

        /// <summary>
        /// Chạy tự động theo trang đang mở (xem tiêu đề trang):
        /// - "Login": làm đầy đủ — đăng nhập (Terminal ID, Shift, Login) → nhập MSNV → nhập PO.
        /// - "PO entry": đã đăng nhập và đã có MSNV → chỉ nhập PO.
        /// - "Operator entry": đã đăng nhập → nhập MSNV → nhập PO.
        /// - Trang khác: không làm gì, trả về lỗi.
        /// Trả về null nếu thành công, ngược lại trả về thông báo lỗi.
        /// </summary>
        public async Task<string?> RunAsync(string terminalId, string msnv, string po, string shift = "1S")
        {
            if (!await WaitPageReadyAsync(TimeSpan.FromSeconds(10)))
                return "Trang web chưa tải xong.";

            // Trang đang dừng ở màn hình "You are logged in another process" (lần trước Login bị chặn)
            if (await IsLoggedInAnotherProcessAsync())
                return LoggedInAnotherProcessError(terminalId);

            var title = (await GetPageTitleAsync()).ToLowerInvariant();

            if (title.Contains(PoEntryTitle.ToLowerInvariant()))
                return await EnterPoAsync(po, "trang PO entry chưa hiện nhãn PO");

            if (title.Contains(OperatorEntryTitle.ToLowerInvariant()))
                return await EnterOperatorAndPoAsync(msnv, po);

            if (title.Contains(LoginTitle.ToLowerInvariant()))
            {
                var loginError = await LoginAsync(terminalId, shift);
                if (loginError is not null)
                    return loginError;
                return await EnterOperatorAndPoAsync(msnv, po);
            }

            return $"Trang hiện tại có tiêu đề \"{title}\" — không phải \"{LoginTitle}\" hay \"{PoEntryTitle}\" nên không tự động.";
        }

        /// <summary>Tiêu đề trang: #lblTitle (các trang sau Login) hoặc .titleCommon (trang Login không có #lblTitle).</summary>
        private async Task<string> GetPageTitleAsync()
        {
            var res = await EvalAsync($$"""
                (() => {
                  const el = document.querySelector({{Js(TitleSelector)}}) || document.querySelector('.titleCommon');
                  return el ? el.textContent.replace(/\s+/g, ' ').trim() : '';
                })()
                """);
            return res?.ValueKind == JsonValueKind.String ? res.Value.GetString() ?? "" : "";
        }

        /// <summary>
        /// Trang Login: click #MyVersion, chờ 1s, nhấn Backspace 1 lần, nhập Terminal ID (Enter để trang xác nhận),
        /// mở combobox và chọn Shift, chờ 1s rồi chờ tối đa 30s cho #txtDutyYmd có dữ liệu, nhấn #btnLogin,
        /// rồi chờ tối đa 30s cho #lblTitle chứa "Operator entry".
        /// </summary>
        private async Task<string?> LoginAsync(string terminalId, string shift)
        {
            // 1) Click vào label #MyVersion
            var label = await EvalAsync($$"""
                (() => {
                  const el = document.querySelector({{Js(MyVersionSelector)}});
                  if (!el) return null;
                  el.scrollIntoView({ block: 'center' });
                  const r = el.getBoundingClientRect();
                  return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
                })()
                """);
            if (label is not { ValueKind: JsonValueKind.Object } lb)
                return "Không tìm thấy label " + MyVersionSelector + " trên trang hiện tại.";
            await ClickAsync(lb.GetProperty("x").GetDouble(), lb.GetProperty("y").GetDouble());

            // 2) Chờ 1 giây rồi nhấn phím xóa 1 lần
            await Task.Delay(1000);
            await PressKeyAsync("Backspace", 8);

            await Task.Delay(2000);

            // 3) Nhập Terminal ID theo đúng cơ chế của trang (bzbarcode.js):
            //    focus ô txtTerminalID → trang đặt keyBuffer = 'TR'; gõ từng phím (keydown) → cộng vào keyBuffer;
            //    Enter → trang gọi TerminalIDvalidate() để điền ô và postback nạp danh sách Shift.
            var focused = await EvalAsync($$"""
                (() => {
                  const el = document.querySelector({{Js(TerminalInputSelector)}});
                  if (!el) return false;
                  el.focus();
                  el.value = '';
                  if (typeof keyBuffer !== 'undefined')
                    keyBuffer = (typeof BCD_TERMINAL !== 'undefined') ? BCD_TERMINAL : 'TR';
                  return document.activeElement === el;
                })()
                """);
            if (focused?.ValueKind != JsonValueKind.True)
                return "Không đưa được con trỏ vào ô TerminalID (" + TerminalInputSelector + ").";

            // Mã máy dạng barcode "TRTB23333": trang đã tự thêm tiền tố "TR" vào keyBuffer khi focus ô,
            // nên chỉ gõ phần sau "TR" (TB23333), tránh thành "TRTRTB23333".
            await TypeAndEnterAsync(StripTerminalPrefix(terminalId));
            await Task.Delay(3000);

            // 4) Chờ postback của Terminal ID xong
            if (!await WaitPageReadyAsync(TimeSpan.FromSeconds(15)))
                return "Trang web chưa xử lý xong Terminal ID.";

            // 5) Chọn Shift
            var shiftError = await SelectShiftAsync(shift);
            if (shiftError is not null)
                return shiftError;

            // 6) Chọn xong chờ 1s, rồi chờ tối đa 30s cho ô AttendanceDate (#txtDutyYmd) có dữ liệu
            await Task.Delay(1000);
            if (!await WaitDutyDateAsync(TimeSpan.FromSeconds(30)))
                return "Sau 30 giây ô AttendanceDate (" + DutyDateSelector + ") vẫn trống nên chưa nhấn Login.";

            // 7) Có ngày rồi → nhấn nút Login
            if (!await WaitPageReadyAsync(TimeSpan.FromSeconds(10)))
                return "Trang web chưa xử lý xong Shift nên chưa nhấn Login.";
            var login = await EvalAsync($$"""
                (() => {
                  const el = document.querySelector({{Js(LoginButtonSelector)}});
                  if (!el || el.disabled) return null;
                  el.scrollIntoView({ block: 'center' });
                  const r = el.getBoundingClientRect();
                  return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
                })()
                """);
            if (login is not { ValueKind: JsonValueKind.Object } lg)
                return "Không tìm thấy hoặc không bấm được nút Login (" + LoginButtonSelector + ").";
            await ClickAsync(lg.GetProperty("x").GetDouble(), lg.GetProperty("y").GetDouble());

            // 8) Chờ tối đa 30s cho trang mới có tiêu đề #lblTitle chứa "Operator entry".
            //    Nếu trang báo "You are logged in another process" → dừng, không làm các bước sau.
            await Task.Delay(500);
            switch (await WaitAfterLoginAsync(TimeSpan.FromSeconds(30)))
            {
                case AfterLogin.LoggedInAnotherProcess:
                    return LoggedInAnotherProcessError(terminalId);
                case AfterLogin.Timeout:
                    return $"Sau 30 giây trang không hiện \"{OperatorEntryTitle}\" ({TitleSelector}) nên chưa nhập MSNV.";
            }
            if (!await WaitPageReadyAsync(TimeSpan.FromSeconds(10)))
                return "Trang Operator entry chưa tải xong.";

            return null;
        }

        /// <summary>
        /// Đã đăng nhập: nhấn vào #txtOperatorID, nhập MSNV và nhấn Enter, rồi nhập PO (EnterPoAsync).
        /// </summary>
        private async Task<string?> EnterOperatorAndPoAsync(string msnv, string po)
        {
            // 9) Nhấn vào ô #txtOperatorID rồi nhập MSNV
            var op = await EvalAsync($$"""
                (() => {
                  const el = document.querySelector({{Js(OperatorInputSelector)}});
                  if (!el || el.disabled) return null;
                  el.scrollIntoView({ block: 'center' });
                  const r = el.getBoundingClientRect();
                  return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
                })()
                """);
            if (op is not { ValueKind: JsonValueKind.Object } o)
                return "Không tìm thấy ô nhập MSNV (" + OperatorInputSelector + ").";
            await ClickAsync(o.GetProperty("x").GetDouble(), o.GetProperty("y").GetDouble());
            await Task.Delay(200);

            // Đảm bảo con trỏ đang ở ô MSNV; nếu ô có sẵn chữ thì bôi đen để gõ đè lên
            var ready = await EvalAsync($$"""
                (() => {
                  const el = document.querySelector({{Js(OperatorInputSelector)}});
                  if (!el) return false;
                  if (document.activeElement !== el) el.focus();
                  el.select();
                  return document.activeElement === el;
                })()
                """);
            if (ready?.ValueKind != JsonValueKind.True)
                return "Không đưa được con trỏ vào ô MSNV (" + OperatorInputSelector + ").";

            // Gõ MSNV rồi nhấn Enter
            await TypeAndEnterAsync(msnv);

            // 10) MSNV hợp lệ thì trang hiện #lblPO → nhập PO
            await Task.Delay(500);
            return await EnterPoAsync(po, $"có thể MSNV \"{msnv}\" không hợp lệ");
        }

        /// <summary>
        /// Chờ tối đa 30s cho #lblPO hiện ra, nhấn vào, nhập số PO và nhấn Enter.
        /// <paramref name="missingHint"/>: gợi ý nguyên nhân khi không thấy #lblPO.
        /// </summary>
        private async Task<string?> EnterPoAsync(string po, string missingHint)
        {
            var lblPo = await WaitVisibleAsync(PoLabelSelector, TimeSpan.FromSeconds(30));
            if (lblPo is not { } pp)
                return $"Sau 30 giây không thấy {PoLabelSelector} ({missingHint}) nên chưa nhập PO.";
            await ClickAsync(pp.x, pp.y);
            await Task.Delay(300);

            // Gõ PO rồi nhấn Enter
            await TypeAndEnterAsync(po);

            // Chờ tối đa 30s cho #lblInstructionNo xuất hiện (có số PO) → mới coi là xong phần web
            // (app chỉ chuyển sang mở WorkSupportSystem khi hàm này trả về null)
            await Task.Delay(500);
            if (!await WaitInstructionNoAsync(TimeSpan.FromSeconds(30)))
                return $"Sau 30 giây trang không hiện {InstructionNoSelector} sau khi nhập PO {po}.\n" +
                       "Chưa mở WorkSupportSystem. Vui lòng kiểm tra lại thông tin (Terminal ID, MSNV, PO) trên trang web.";

            return null;
        }

        /// <summary>Chờ đến khi #lblInstructionNo có trên trang và có nội dung.</summary>
        private async Task<bool> WaitInstructionNoAsync(TimeSpan timeout)
        {
            var script = $$"""
                (() => {
                  const el = document.querySelector({{Js(InstructionNoSelector)}});
                  return !!el && el.textContent.trim() !== '';
                })()
                """;
            var deadline = DateTime.Now + timeout;
            while (DateTime.Now < deadline)
            {
                if (!_isNavigating() && (await EvalAsync(script))?.ValueKind == JsonValueKind.True)
                    return true;
                await Task.Delay(200);
            }
            return false;
        }

        /// <summary>Chờ phần tử hiện ra trên trang; trả về tọa độ tâm của nó.</summary>
        private async Task<(double x, double y)?> WaitVisibleAsync(string selector, TimeSpan timeout)
        {
            var script = $$"""
                (() => {
                  const el = document.querySelector({{Js(selector)}});
                  if (!el) return null;
                  const s = getComputedStyle(el);
                  if (s.display === 'none' || s.visibility === 'hidden') return null;
                  el.scrollIntoView({ block: 'center' });
                  const r = el.getBoundingClientRect();
                  if (r.width === 0 || r.height === 0) return null;
                  return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
                })()
                """;
            var deadline = DateTime.Now + timeout;
            while (DateTime.Now < deadline)
            {
                if (!_isNavigating() && await EvalAsync(script) is { ValueKind: JsonValueKind.Object } r)
                    return (r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble());
                await Task.Delay(200);
            }
            return null;
        }

        /// <summary>
        /// Gõ chuỗi như bàn phím thật rồi nhấn Enter.
        /// App gõ rất nhanh giống máy quét → tạm tắt bộ bắt mã quét trong lúc gõ để không bị nhận nhầm là người dùng quét,
        /// và để Enter đi thẳng vào trang.
        /// </summary>
        private async Task TypeAndEnterAsync(string text)
        {
            await SetScanHookPausedAsync(true);
            try
            {
                foreach (var c in text)
                    await TypeCharAsync(c);
                await Task.Delay(100);
                await PressEnterAsync();
            }
            finally
            {
                await SetScanHookPausedAsync(false);
            }
        }

        private Task SetScanHookPausedAsync(bool paused) =>
            EvalAsync($"window.__autoScanPaused = {(paused ? "true" : "false")};");

        /// <summary>Chờ đến khi #lblTitle chứa đoạn chữ cần tìm (không phân biệt hoa thường).</summary>
        private enum AfterLogin { OperatorEntry, LoggedInAnotherProcess, Timeout }

        /// <summary>
        /// Câu trang web hiện khi Terminal ID đang đăng nhập ở nơi khác:
        /// "You are logged in another process. Let me read the administrator ID if you use this process."
        /// So khớp không phân biệt hoa thường, bỏ khoảng trắng thừa.
        /// </summary>
        public const string LoggedInAnotherProcessText = "logged in another process";

        private static string LoggedInAnotherProcessError(string terminalId) =>
            $"Trang web báo: \"You are logged in another process.\"\n" +
            $"Terminal ID \"{terminalId}\" đang đăng nhập ở nơi khác (tab khác trong app, máy khác hoặc trình duyệt khác).\n" +
            "Vui lòng kiểm tra lại thông tin (Terminal ID, MSNV), đăng xuất ở nơi kia hoặc liên hệ quản lý rồi quét lại PO.";

        private static readonly string LoggedInAnotherProcessJs = $$"""
            (() => {
              const t = (document.body ? document.body.innerText : '').replace(/\s+/g, ' ').toLowerCase();
              return t.includes({{Js(LoggedInAnotherProcessText)}});
            })()
            """;

        /// <summary>Trang hiện tại có đang hiện câu "You are logged in another process" không.</summary>
        private async Task<bool> IsLoggedInAnotherProcessAsync() =>
            (await EvalAsync(LoggedInAnotherProcessJs))?.ValueKind == JsonValueKind.True;

        /// <summary>Sau khi nhấn Login: chờ trang "Operator entry" hoặc màn hình "logged in another process".</summary>
        private async Task<AfterLogin> WaitAfterLoginAsync(TimeSpan timeout)
        {
            var deadline = DateTime.Now + timeout;
            while (DateTime.Now < deadline)
            {
                if (!_isNavigating())
                {
                    if (await IsLoggedInAnotherProcessAsync())
                        return AfterLogin.LoggedInAnotherProcess;
                    if ((await GetPageTitleAsync()).Contains(OperatorEntryTitle, StringComparison.OrdinalIgnoreCase))
                        return AfterLogin.OperatorEntry;
                }
                await Task.Delay(200);
            }
            return AfterLogin.Timeout;
        }

        private async Task<bool> WaitTitleAsync(string text, TimeSpan timeout)
        {
            var script = $$"""
                (() => {
                  const el = document.querySelector({{Js(TitleSelector)}});
                  return !!el && el.textContent.toLowerCase().includes({{Js(text.ToLowerInvariant())}});
                })()
                """;
            var deadline = DateTime.Now + timeout;
            while (DateTime.Now < deadline)
            {
                if (!_isNavigating() && (await EvalAsync(script))?.ValueKind == JsonValueKind.True)
                    return true;
                await Task.Delay(200);
            }
            return false;
        }

        /// <summary>Chờ đến khi ô AttendanceDate (#txtDutyYmd) có dữ liệu (khác rỗng).</summary>
        private async Task<bool> WaitDutyDateAsync(TimeSpan timeout)
        {
            var script = $$"""
                (() => {
                  const el = document.querySelector({{Js(DutyDateSelector)}});
                  return !!el && el.value.trim() !== '';
                })()
                """;
            var deadline = DateTime.Now + timeout;
            while (DateTime.Now < deadline)
            {
                if (!_isNavigating() && (await EvalAsync(script))?.ValueKind == JsonValueKind.True)
                    return true;
                await Task.Delay(200);
            }
            return false;
        }

        /// <summary>Mở combobox Shift (▼) và chọn dòng cần chọn. Trả về null nếu thành công, ngược lại là thông báo lỗi.</summary>
        private async Task<string?> SelectShiftAsync(string shift)
        {
            // Giống thao tác tay: mở combobox Shift (▼) rồi chạm vào dòng SF1.
            //    Combobox này làm cho màn hình cảm ứng (ddlTouch.js): click chuột sẽ ẩn danh sách trước khi kịp chọn,
            //    nên dòng SF1 được chọn bằng sự kiện chạm (touch).
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                if (!await WaitPageReadyAsync(TimeSpan.FromSeconds(10)))
                    return "Trang web chưa tải xong.";
                if (await IsShiftSelectedAsync(shift))
                    return null;

                var menu = await EvalAsync($$"""
                    (() => {
                      const el = document.querySelector({{Js(ShiftMenuSelector)}});
                      if (!el) return null;
                      el.scrollIntoView({ block: 'center' });
                      const r = el.getBoundingClientRect();
                      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
                    })()
                    """);
                if (menu is not { ValueKind: JsonValueKind.Object } m)
                    return "Không tìm thấy combobox Shift (" + ShiftMenuSelector + ").";

                await ClickAsync(m.GetProperty("x").GetDouble(), m.GetProperty("y").GetDouble());

                var item = await WaitForShiftItemAsync(shift, TimeSpan.FromSeconds(3));
                if (item is not { } p)
                {
                    await Task.Delay(1000);   // danh sách có thể chưa nạp xong → đóng/mở lại ở lần sau
                    continue;
                }

                await TapAsync(p.x, p.y);
                if (await WaitShiftSelectedAsync(shift, TimeSpan.FromSeconds(8)))
                    return null;
            }

            // Dự phòng cuối: làm đúng việc trang làm khi chạm vào dòng (ddlTouch.js) rồi gửi lên server
            await EvalAsync($$"""
                (() => {
                  const want = {{Js(shift)}};
                  {{ShiftItemMatchJs}}
                  const li = [...document.querySelectorAll({{Js(ShiftListSelector)}})].find(isItem);
                  if (!li) return false;
                  const key = li.querySelector('input[type=hidden]');
                  const span = li.querySelector('span');
                  document.querySelector('#{{ShiftPrefix}}_hidSelKeys').value = key ? key.value : '';
                  document.querySelector('#{{ShiftPrefix}}_hidSelItems').value = span ? span.textContent : '';
                  document.querySelector('#{{ShiftPrefix}}_btnListSelect').click();
                  return true;
                })()
                """);
            if (await WaitShiftSelectedAsync(shift, TimeSpan.FromSeconds(10)))
                return null;

            return $"Không chọn được dòng \"{shift}\" trong combobox Shift. Các dòng app thấy: {await DescribeShiftListAsync()}";
        }

        /// <summary>Liệt kê các dòng trong danh sách Shift (id = chữ | mã ẩn | đang hiện?) để chẩn đoán khi lỗi.</summary>
        private async Task<string> DescribeShiftListAsync()
        {
            var res = await EvalAsync($$"""
                (() => {
                  const lis = [...document.querySelectorAll({{Js(ShiftListSelector)}})];
                  if (!lis.length) return '(không có dòng nào khớp selector {{ShiftListSelector}})';
                  return lis.slice(0, 15).map(li => {
                    const k = li.querySelector('input[type=hidden]');
                    const r = li.getBoundingClientRect();
                    return '[' + li.textContent.replace(/\s+/g, ' ').trim() + ' | ' + (k ? k.value : '-') +
                           ' | ' + (r.width > 0 ? 'hiện' : 'ẩn') + ']';
                  }).join(' ');
                })()
                """);
            return res?.ValueKind == JsonValueKind.String ? res.Value.GetString() ?? "" : "(không đọc được)";
        }

        // ====== Hàm hỗ trợ ======

        /// <summary>Chờ đến khi danh sách Shift hiện ra và có dòng cần chọn; trả về tọa độ tâm của dòng đó.</summary>
        private async Task<(double x, double y)?> WaitForShiftItemAsync(string shift, TimeSpan timeout)
        {
            var script = $$"""
                (() => {
                  const want = {{Js(shift)}};
                  const visible = el => {
                    const r = el.getBoundingClientRect(), s = getComputedStyle(el);
                    return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none';
                  };
                  {{ShiftItemMatchJs}}
                  // Dòng <li> SF1 trong danh sách Shift đang hiển thị
                  const el = [...document.querySelectorAll({{Js(ShiftListSelector)}})]
                    .find(li => isItem(li) && visible(li));
                  if (!el) return null;
                  el.scrollIntoView({ block: 'nearest' });
                  const r = el.getBoundingClientRect();
                  return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
                })()
                """;

            var deadline = DateTime.Now + timeout;
            while (DateTime.Now < deadline)
            {
                if (!_isNavigating())
                {
                    var res = await EvalAsync(script);
                    if (res is { ValueKind: JsonValueKind.Object } r)
                        return (r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble());
                }
                await Task.Delay(100);
            }
            return null;
        }

        /// <summary>Chờ trang tải xong và không còn postback (kể cả UpdatePanel của ASP.NET).</summary>
        private async Task<bool> WaitPageReadyAsync(TimeSpan timeout)
        {
            const string script = """
                (() => {
                  if (document.readyState !== 'complete') return false;
                  try {
                    if (window.Sys && Sys.WebForms && Sys.WebForms.PageRequestManager &&
                        Sys.WebForms.PageRequestManager.getInstance().get_isInAsyncPostBack()) return false;
                  } catch (e) { }
                  return true;
                })()
                """;
            var deadline = DateTime.Now + timeout;
            while (DateTime.Now < deadline)
            {
                if (!_isNavigating() && (await EvalAsync(script))?.ValueKind == JsonValueKind.True)
                    return true;
                await Task.Delay(100);
            }
            return false;
        }

        /// <summary>Shift đã được chọn đúng chưa (giá trị server trả về trong SelVal, hoặc chữ đang hiển thị).</summary>
        private async Task<bool> IsShiftSelectedAsync(string shift)
        {
            var res = await EvalAsync($$"""
                (() => {
                  const want = {{Js(shift.ToUpperInvariant())}};
                  const v = document.querySelector('#{{ShiftPrefix}}_SelVal');
                  const s = document.querySelector({{Js(ShiftSelectedSelector)}});
                  return (!!v && v.value.trim().toUpperCase() === want) ||
                         (!!s && s.textContent.trim().toUpperCase() === want);
                })()
                """);
            return res?.ValueKind == JsonValueKind.True;
        }

        private async Task<bool> WaitShiftSelectedAsync(string shift, TimeSpan timeout)
        {
            var deadline = DateTime.Now + timeout;
            while (DateTime.Now < deadline)
            {
                await Task.Delay(200);
                if (await WaitPageReadyAsync(TimeSpan.FromSeconds(1)) && await IsShiftSelectedAsync(shift))
                    return true;
            }
            return false;
        }

        /// <summary>Chạm (touch) vào 1 điểm như ngón tay trên màn hình cảm ứng.</summary>
        private async Task TapAsync(double x, double y)
        {
            // Bật hỗ trợ cảm ứng cho trang (máy không có màn hình cảm ứng vẫn nhận được sự kiện touch)
            await _core.CallDevToolsProtocolMethodAsync("Emulation.setTouchEmulationEnabled",
                JsonSerializer.Serialize(new { enabled = true, maxTouchPoints = 1 }));
            await _core.CallDevToolsProtocolMethodAsync("Input.dispatchTouchEvent",
                JsonSerializer.Serialize(new { type = "touchStart", touchPoints = new[] { new { x, y } } }));
            await Task.Delay(80);
            await _core.CallDevToolsProtocolMethodAsync("Input.dispatchTouchEvent",
                JsonSerializer.Serialize(new { type = "touchEnd", touchPoints = Array.Empty<object>() }));
        }

        /// <summary>Nhấn Enter như phím thật (keydown keyCode 13 + ký tự xuống dòng).</summary>
        private async Task PressEnterAsync()
        {
            await _core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                JsonSerializer.Serialize(new
                {
                    type = "keyDown", key = "Enter", code = "Enter", text = "\r", unmodifiedText = "\r",
                    windowsVirtualKeyCode = 13, nativeVirtualKeyCode = 13
                }));
            await _core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                JsonSerializer.Serialize(new
                {
                    type = "keyUp", key = "Enter", code = "Enter",
                    windowsVirtualKeyCode = 13, nativeVirtualKeyCode = 13
                }));
        }

        /// <summary>Gõ 1 ký tự như bàn phím thật (có keydown với keyCode đúng để bzbarcode.js của trang bắt được).</summary>
        private async Task TypeCharAsync(char c)
        {
            int vk;
            string code;
            if (char.IsAsciiLetter(c)) { vk = char.ToUpperInvariant(c); code = "Key" + char.ToUpperInvariant(c); }
            else if (char.IsAsciiDigit(c)) { vk = c; code = "Digit" + c; }
            else if (c == '-') { vk = 189; code = "Minus"; }
            else if (c == '.') { vk = 190; code = "Period"; }
            else { vk = 0; code = ""; }

            var text = c.ToString();
            await _core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                JsonSerializer.Serialize(new
                {
                    type = "keyDown", key = text, code, text, unmodifiedText = text,
                    windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk
                }));
            await _core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                JsonSerializer.Serialize(new
                {
                    type = "keyUp", key = text, code,
                    windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk
                }));
            await Task.Delay(20);
        }

        private async Task PressKeyAsync(string key, int virtualKeyCode)
        {
            foreach (var type in new[] { "rawKeyDown", "keyUp" })
                await _core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                    JsonSerializer.Serialize(new { type, key, code = key, windowsVirtualKeyCode = virtualKeyCode }));
        }

        private async Task ClickAsync(double x, double y)
        {
            await _core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                JsonSerializer.Serialize(new { type = "mouseMoved", x, y }));
            foreach (var type in new[] { "mousePressed", "mouseReleased" })
                await _core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    JsonSerializer.Serialize(new { type, x, y, button = "left", clickCount = 1 }));
        }

        /// <summary>Chạy JS và trả về kết quả dạng JSON (null nếu lỗi, ví dụ đang chuyển trang).</summary>
        private async Task<JsonElement?> EvalAsync(string script)
        {
            try
            {
                var json = await _core.ExecuteScriptAsync(script);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Bỏ tiền tố "TR" (mã barcode Terminal) ở đầu mã máy: "TRTB23333" → "TB23333".</summary>
        public static string StripTerminalPrefix(string terminalId)
        {
            var id = terminalId.Trim();
            return id.StartsWith("TR", StringComparison.OrdinalIgnoreCase) && id.Length > 2 ? id[2..] : id;
        }

        /// <summary>Chuyển chuỗi C# thành chuỗi JS an toàn (có dấu nháy).</summary>
        private static string Js(string s) => JsonSerializer.Serialize(s);
    }
}
