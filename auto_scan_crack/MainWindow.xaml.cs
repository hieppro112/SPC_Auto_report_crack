using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace auto_scan_crack
{
    public partial class MainWindow : Window
    {
        private const string HomeUrl = "http://10.4.24.117:8441/Login.aspx";

        // Lưu cookie / phiên đăng nhập cố định để mở lại app không phải đăng nhập lại
        private static readonly string UserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "auto_scan_crack", "WebView2");

        // Terminal ID lưu riêng theo từng máy
        private static readonly string TerminalIdFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "auto_scan_crack", "terminal_id.txt");

        // MSNV (mã số nhân viên) lưu lại cho lần mở app sau
        private static readonly string MsnvFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "auto_scan_crack", "msnv.txt");

        private WebAutomation? _automation;
        private bool _navigating;
        private bool _busy;   // đang chạy tự động → bỏ qua lần quét chồng lên

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTerminalId();
            txtMsnv.Text = LoadText(MsnvFile, "MSNV");
            InitPoTimer();
            // Mã quét/gõ được tự phân loại dù con trỏ ở ô nào → luôn để con trỏ ở ô QUÉT MÃ cho dễ thấy
            txtPO.Focus();
            if (string.IsNullOrWhiteSpace(txtTerminalId.Text) || string.IsNullOrWhiteSpace(txtMsnv.Text))
                SetStatus("Quét mã máy (TRTB…) và MSNV trước khi quét PO.");

            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, UserDataFolder);
                await webView.EnsureCoreWebView2Async(env);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                SetStatus("Máy chưa cài Microsoft Edge WebView2 Runtime.");
                MessageBox.Show(this,
                    "Máy chưa cài Microsoft Edge WebView2 Runtime.\nVui lòng liên hệ IT để cài đặt.",
                    "Thiếu WebView2", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            catch (Exception ex)
            {
                SetStatus("Lỗi khởi tạo trình duyệt: " + ex.Message);
                return;
            }

            var core = webView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = true;          // tắt khi phát hành nếu cần
            core.Settings.IsStatusBarEnabled = false;

            _automation = new WebAutomation(core, () => _navigating);

            // Bắt mã quét khi con trỏ đang nằm trong trang web
            await core.AddScriptToExecuteOnDocumentCreatedAsync(WebAutomation.ScanHookScript);
            core.WebMessageReceived += Core_WebMessageReceived;

            core.NavigationStarting += (_, args) =>
            {
                _navigating = true;
                if (!_busy) SetStatus("Đang tải...");
                lblUrl.Text = args.Uri;
            };
            core.NavigationCompleted += (_, args) =>
            {
                _navigating = false;
                lblUrl.Text = core.Source;
                if (_busy) return;
                SetStatus(args.IsSuccess
                    ? "Sẵn sàng"
                    : $"Không tải được trang ({args.WebErrorStatus}). Kiểm tra kết nối tới máy chủ.");
            };
            // Link mở cửa sổ mới (target=_blank, window.open) → mở ngay trong khung hiện tại
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                core.Navigate(args.Uri);
            };

            core.Navigate(HomeUrl);
        }

        // ===== Quét PO =====

        // Mọi phím gõ/quét trong app (con trỏ ở ô nào cũng vậy) đều gom vào bộ đệm này, hiện ở ô QUÉT MÃ;
        // nhấn Enter thì phân loại mã (mã máy / MSNV / PO) rồi đưa vào đúng chỗ.
        private readonly System.Text.StringBuilder _scanBuffer = new();
        private DateTime _lastScanKeyAt = DateTime.MinValue;

        // Ngừng gõ quá lâu thì coi phần đã gõ là bỏ dở → bắt đầu mã mới
        private static readonly TimeSpan ScanBufferTimeout = TimeSpan.FromSeconds(3);

        private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = true;   // không cho chữ đi thẳng vào ô đang có con trỏ
            if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;

            var now = DateTime.Now;
            if (now - _lastScanKeyAt > ScanBufferTimeout) _scanBuffer.Clear();
            _lastScanKeyAt = now;

            _scanBuffer.Append(e.Text.ToUpperInvariant());
            ShowScanBuffer();
        }

        private void SubmitScanBuffer()
        {
            var code = _scanBuffer.ToString().Trim();
            ClearScanBuffer();
            if (code.Length > 0)
                HandleScannedCode(code);
        }

        private void ClearScanBuffer()
        {
            _scanBuffer.Clear();
            ShowScanBuffer();
        }

        private void ShowScanBuffer()
        {
            txtPO.Text = _scanBuffer.ToString();
            txtPO.CaretIndex = txtPO.Text.Length;
        }

        /// <summary>Trang web báo về khi máy quét gõ mã (mã máy / MSNV / PO) lúc con trỏ đang ở trong web.</summary>
        private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
                var root = doc.RootElement;
                if (root.GetProperty("type").GetString() == "scan")
                    HandleScannedCode(root.GetProperty("code").GetString() ?? "");
            }
            catch
            {
                // bỏ qua tin nhắn không đúng định dạng
            }
        }

        /// <summary>
        /// Phân loại mã vừa quét:
        /// - "TRTB…"     → mã máy: điền vào ô Terminal ID và lưu lại.
        /// - 4–5 chữ số  → MSNV: điền vào ô MSNV và lưu lại.
        /// - 12 chữ số   → PO: bắt đầu chạy tự động.
        /// - Khác        → báo lỗi.
        /// </summary>
        private void HandleScannedCode(string raw)
        {
            var code = raw.Trim().ToUpperInvariant();
            switch (ScanClassifier.Classify(code))
            {
                case ScanKind.Terminal:
                    txtTerminalId.Text = code;
                    SaveTerminalId();
                    SetStatus($"Đã quét mã máy: {code}  ({DateTime.Now:HH:mm:ss})");
                    txtPO.Focus();
                    break;

                case ScanKind.Msnv:
                    txtMsnv.Text = code;
                    SaveText(MsnvFile, code, "MSNV");
                    SetStatus($"Đã quét MSNV: {code}  ({DateTime.Now:HH:mm:ss})");
                    txtPO.Focus();
                    break;

                case ScanKind.Po:
                    _ = HandlePoScannedAsync(code);
                    break;

                default:
                    ShowError("MÃ KHÔNG HỢP LỆ",
                        $"Mã \"{code}\" không phải mã máy (TRTB…), MSNV (4–5 chữ số) hay PO (12 chữ số).", txtPO);
                    break;
            }
        }

        /// <summary>
        /// Quét xong 1 mã PO (12 ký tự): xóa ô TerminalID trên web, gõ Terminal ID, chọn Shift, chờ AttendanceDate rồi nhấn Login,
        /// chờ trang Operator entry rồi nhập MSNV, chờ #lblPO rồi nhập số PO;
        /// cuối cùng chờ 10s rồi nhập PO vào WorkSupportSystem (chưa mở thì báo lỗi).
        /// </summary>
        private async Task HandlePoScannedAsync(string po)
        {
            po = po.Trim().ToUpperInvariant();
            if (ScanClassifier.Classify(po) != ScanKind.Po) return;

            if (_busy)
            {
                ShowError("ĐANG CHẠY PO KHÁC", $"PO {lblCurrentPO.Text} đang chạy. Vui lòng đợi xong rồi mới quét PO {po}.");
                return;
            }

            var terminalId = txtTerminalId.Text.Trim();
            if (terminalId.Length == 0)
            {
                ShowError("CHƯA NHẬP TERMINAL ID", "Vui lòng nhập Terminal ID trước khi quét PO.", txtTerminalId);
                return;
            }
            var msnv = txtMsnv.Text.Trim();
            if (msnv.Length == 0)
            {
                ShowError("CHƯA NHẬP MSNV", "Vui lòng nhập MSNV trước khi quét PO.", txtMsnv);
                return;
            }
            if (_automation is null)
            {
                ShowError("TRÌNH DUYỆT CHƯA SẴN SÀNG", "Vui lòng chờ trang web tải xong rồi quét lại PO.", txtPO);
                return;
            }
            _busy = true;
            lblCurrentPO.Text = po;

            // Báo xanh 2 giây: đã bắt đầu chạy, không quét thêm PO
            ShowInfo("BẮT ĐẦU CHẠY PO", $"PO: {po}\nVui lòng không quét thêm PO cho đến khi hoàn tất.", 2);

            SetStatus($"Đã quét PO: {po} — đang điền Terminal ID, chọn Shift, Login, nhập MSNV và PO...");
            try
            {
                webView.Focus();   // để phím gõ tự động đi vào trang web
                var error = await _automation.RunAsync(terminalId, msnv, po);
                if (error is null)
                {
                    SetStatus($"PO: {po} | Đã Login (Terminal \"{terminalId}\"), nhập MSNV \"{msnv}\" và nhập PO  ({DateTime.Now:HH:mm:ss})");
                    await SendPoToWorkSupportAsync(po);
                }
                else
                    ShowError("LỖI TỰ ĐỘNG", $"PO: {po}\n{error}");
            }
            catch (Exception ex)
            {
                ShowError("LỖI TỰ ĐỘNG", $"PO: {po}\n{ex.Message}");
            }
            finally
            {
                _busy = false;
            }
        }

        // ===== WorkSupportSystem =====

        private const int WorkSupportDelaySeconds = 10;

        /// <summary>
        /// Web xong → nhập PO vào WorkSupportSystem: đang mở thì chờ 10s, đưa cửa sổ lên trước, gõ PO + Enter;
        /// chưa mở thì hiện cảnh báo yêu cầu người dùng mở.
        /// </summary>
        private async Task SendPoToWorkSupportAsync(string po)
        {
            if (WorkSupportApp.FindRunning() is null)
            {
                ShowError("CHƯA MỞ WORKSUPPORTSYSTEM",
                    $"Vui lòng mở WorkSupportSystem (加工支援システム) rồi nhập PO {po}.", txtPO);
                return;
            }

            for (int s = WorkSupportDelaySeconds; s > 0; s--)
            {
                SetStatus($"PO: {po} | Sẽ nhập PO vào WorkSupportSystem sau {s} giây...");
                await Task.Delay(1000);
            }

            // Kiểm tra lại: trong 10s người dùng có thể đã tắt app
            using var process = WorkSupportApp.FindRunning();
            if (process is null)
            {
                ShowError("CHƯA MỞ WORKSUPPORTSYSTEM",
                    $"WorkSupportSystem đã bị tắt. Vui lòng mở lại rồi nhập PO {po}.", txtPO);
                return;
            }

            var hwnd = WorkSupportApp.FindMainWindow(process);
            if (hwnd == IntPtr.Zero)
            {
                ShowError("KHÔNG TÌM THẤY CỬA SỔ WORKSUPPORTSYSTEM",
                    $"WorkSupportSystem đang chạy nhưng không có cửa sổ. Vui lòng mở cửa sổ rồi nhập PO {po}.", txtPO);
                return;
            }

            // Chỉ gõ khi chắc chắn WorkSupportSystem đang nhận bàn phím, tránh gõ nhầm vào app khác
            if (!WorkSupportApp.BringToFront(hwnd, process.Id))
            {
                ShowError("KHÔNG ĐƯA ĐƯỢC WORKSUPPORTSYSTEM LÊN TRƯỚC",
                    $"Vui lòng bấm vào cửa sổ WorkSupportSystem rồi nhập PO {po}.", txtPO);
                return;
            }

            await Task.Delay(300);
            await WorkSupportApp.TypeTextAsync(po);
            await Task.Delay(100);
            WorkSupportApp.PressEnter();

            SetStatus($"PO: {po} | Đã nhập PO vào WorkSupportSystem  ({DateTime.Now:HH:mm:ss})");

            // Xong thao tác với WorkSupportSystem → lấy GOODCNT và bắt đầu đếm giờ cho PO từ lúc này
            await StartPoTimerAsync(po, DateTime.Now);
        }

        // ===== Terminal ID =====

        private void LoadTerminalId()
        {
            try
            {
                if (File.Exists(TerminalIdFile))
                    txtTerminalId.Text = File.ReadAllText(TerminalIdFile).Trim();
            }
            catch (Exception ex)
            {
                SetStatus("Không đọc được Terminal ID đã lưu: " + ex.Message);
            }
        }

        private void SaveTerminalId()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TerminalIdFile)!);
                File.WriteAllText(TerminalIdFile, txtTerminalId.Text.Trim());
            }
            catch (Exception ex)
            {
                SetStatus("Không lưu được Terminal ID: " + ex.Message);
            }
        }

        // ===== MSNV =====

        private string LoadText(string file, string name)
        {
            try
            {
                return File.Exists(file) ? File.ReadAllText(file).Trim() : string.Empty;
            }
            catch (Exception ex)
            {
                SetStatus($"Không đọc được {name} đã lưu: " + ex.Message);
                return string.Empty;
            }
        }

        private void SaveText(string file, string value, string name)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, value.Trim());
            }
            catch (Exception ex)
            {
                SetStatus($"Không lưu được {name}: " + ex.Message);
            }
        }

        // ===== Phím tắt & nút =====

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:         // kết thúc 1 mã (máy quét luôn gửi Enter ở cuối)
                    SubmitScanBuffer();
                    e.Handled = true;
                    break;
                case Key.Back:          // xóa 1 ký tự vừa gõ
                    if (_scanBuffer.Length > 0) _scanBuffer.Length--;
                    ShowScanBuffer();
                    e.Handled = true;
                    break;
                case Key.Escape:        // bỏ mã đang gõ dở
                    ClearScanBuffer();
                    e.Handled = true;
                    break;
                case Key.F2:            // đưa con trỏ về ô quét mã
                    txtPO.Focus();
                    e.Handled = true;
                    break;
                case Key.F5:
                    webView.CoreWebView2?.Reload();
                    e.Handled = true;
                    break;
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (webView.CoreWebView2?.CanGoBack == true) webView.CoreWebView2.GoBack();
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e) => webView.CoreWebView2?.Reload();

        private void BtnHome_Click(object sender, RoutedEventArgs e) => webView.CoreWebView2?.Navigate(HomeUrl);

        private void SetStatus(string text) => lblStatus.Text = text;

        private AlertWindow? _info;

        /// <summary>Thông báo màu xanh toàn màn hình, tự đóng sau vài giây.</summary>
        private void ShowInfo(string title, string message, int seconds)
        {
            _info?.Close();
            var info = new AlertWindow(title, message, seconds, "#2E7D32");
            info.Closed += (_, _) =>
            {
                if (_info == info) _info = null;
                Activate();
                if (_busy) webView.Focus();   // đang tự động → trả focus cho trang web
                else txtPO.Focus();
            };
            _info = info;
            info.Show();
        }

        private AlertWindow? _alert;

        /// <summary>
        /// Báo lỗi: ghi lên thanh trạng thái và hiện cảnh báo toàn màn hình
        /// (đóng khi người dùng bấm ĐÓNG / Esc / Enter, hoặc tự đóng sau 5 giây).
        /// Đóng xong thì đưa con trỏ về ô <paramref name="focusAfter"/> nếu có.
        /// </summary>
        private void ShowError(string title, string message, IInputElement? focusAfter = null)
        {
            SetStatus($"{title}: {message.Replace('\n', ' ')}  ({DateTime.Now:HH:mm:ss})");

            _alert?.Close();   // chỉ giữ 1 cảnh báo mới nhất
            // Không đặt Owner: nếu cửa sổ chính bị thu nhỏ thì cảnh báo vẫn hiện
            var alert = new AlertWindow(title, message);
            alert.Closed += (_, _) =>
            {
                if (_alert == alert) _alert = null;
                Activate();
                focusAfter?.Focus();
            };
            _alert = alert;
            alert.Show();
        }
    }
}
