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

        public const int ScanLength = 12;

        /// <summary>
        /// Script chèn vào mọi trang: bắt chuỗi do máy quét gõ (gõ rất nhanh, đủ 12 ký tự, kết thúc bằng Enter)
        /// khi con trỏ đang nằm trong trang web, rồi báo về app.
        /// </summary>
        public static readonly string ScanHookScript = $$"""
            (() => {
              if (window.__autoScanHook) return;
              window.__autoScanHook = true;
              const LEN = {{ScanLength}}, MAX_GAP_MS = 50;   // máy quét gõ < 50ms/ký tự, người gõ tay chậm hơn nhiều
              let buf = '', last = 0;
              window.addEventListener('keydown', e => {
                if (window.__autoScanPaused) { buf = ''; return; }   // app đang tự gõ → bỏ qua
                const now = performance.now();
                if (now - last > MAX_GAP_MS) buf = '';
                last = now;
                if (e.key === 'Enter') {
                  if (buf.length === LEN) {
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

        private readonly CoreWebView2 _core;
        private readonly Func<bool> _isNavigating;

        public WebAutomation(CoreWebView2 core, Func<bool> isNavigating)
        {
            _core = core;
            _isNavigating = isNavigating;
        }

        /// <summary>
        /// Trên trang Login: click #MyVersion, chờ 1s, nhấn Backspace 1 lần, nhập Terminal ID (Enter để trang xác nhận),
        /// mở combobox và chọn Shift, chờ 1s rồi chờ tối đa 30s cho #txtDutyYmd có dữ liệu, sau đó nhấn #btnLogin;
        /// chờ tối đa 30s cho #lblTitle chứa "Operator entry", rồi nhấn vào #txtOperatorID, nhập MSNV và nhấn Enter;
        /// chờ tối đa 30s cho #lblPO hiện ra, nhấn vào, nhập số PO và nhấn Enter.
        /// Trả về null nếu thành công, ngược lại trả về thông báo lỗi.
        /// </summary>
        public async Task<string?> FillLoginAsync(string terminalId, string msnv, string po, string shift = "1S")
        {
            if (!await WaitPageReadyAsync(TimeSpan.FromSeconds(10)))
                return "Trang web chưa tải xong.";

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

            foreach (var c in terminalId)
                await TypeCharAsync(c);

            // Điền mã máy xong → nhấn Enter (như phím Enter thật)
            await Task.Delay(100);
            await PressEnterAsync();
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

            // 8) Chờ tối đa 30s cho trang mới có tiêu đề #lblTitle chứa "Operator entry"
            await Task.Delay(500);
            if (!await WaitTitleAsync(OperatorEntryTitle, TimeSpan.FromSeconds(30)))
                return $"Sau 30 giây trang không hiện \"{OperatorEntryTitle}\" ({TitleSelector}) nên chưa nhập MSNV.";
            if (!await WaitPageReadyAsync(TimeSpan.FromSeconds(10)))
                return "Trang Operator entry chưa tải xong.";

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

            foreach (var c in msnv)
                await TypeCharAsync(c);

            // Gõ MSNV xong → nhấn Enter (như phím Enter thật)
            await Task.Delay(100);
            await PressEnterAsync();

            // 10) MSNV hợp lệ thì trang hiện #lblPO (chờ tối đa 30s) → nhấn vào rồi nhập số PO vừa quét
            await Task.Delay(500);
            var lblPo = await WaitVisibleAsync(PoLabelSelector, TimeSpan.FromSeconds(30));
            if (lblPo is not { } pp)
                return $"Sau 30 giây không thấy {PoLabelSelector} (có thể MSNV \"{msnv}\" không hợp lệ) nên chưa nhập PO.";
            await ClickAsync(pp.x, pp.y);
            await Task.Delay(300);

            // App tự gõ 12 ký tự PO rất nhanh → tạm tắt bộ bắt mã quét để không nhận nhầm là người dùng quét PO mới
            await SetScanHookPausedAsync(true);
            try
            {
                foreach (var c in po)
                    await TypeCharAsync(c);

                // Gõ PO xong → nhấn Enter (vẫn trong lúc tạm tắt bộ bắt mã quét để Enter đi thẳng vào trang)
                await Task.Delay(100);
                await PressEnterAsync();
            }
            finally
            {
                await SetScanHookPausedAsync(false);
            }

            return null;
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

        private Task SetScanHookPausedAsync(bool paused) =>
            EvalAsync($"window.__autoScanPaused = {(paused ? "true" : "false")};");

        /// <summary>Chờ đến khi #lblTitle chứa đoạn chữ cần tìm (không phân biệt hoa thường).</summary>
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

        /// <summary>Chuyển chuỗi C# thành chuỗi JS an toàn (có dấu nháy).</summary>
        private static string Js(string s) => JsonSerializer.Serialize(s);
    }
}
