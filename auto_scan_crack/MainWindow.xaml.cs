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
            // Ô nào còn trống thì đưa con trỏ vào ô đó trước, đủ rồi thì vào ô PO
            if (string.IsNullOrWhiteSpace(txtTerminalId.Text)) txtTerminalId.Focus();
            else if (string.IsNullOrWhiteSpace(txtMsnv.Text)) txtMsnv.Focus();
            else txtPO.Focus();

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

        private void TxtPO_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;

            var code = txtPO.Text.Trim();
            txtPO.Clear();
            if (code.Length == 0) return;

            if (code.Length != WebAutomation.ScanLength)
            {
                ShowError("MÃ PO KHÔNG HỢP LỆ",
                    $"Mã \"{code}\" có {code.Length} ký tự — mã PO phải đúng {WebAutomation.ScanLength} ký tự.", txtPO);
                return;
            }
            _ = HandlePoScannedAsync(code);
        }

        /// <summary>Trang web báo về khi máy quét gõ chuỗi 12 ký tự lúc con trỏ đang ở trong web.</summary>
        private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
                var root = doc.RootElement;
                if (root.GetProperty("type").GetString() == "scan")
                    _ = HandlePoScannedAsync(root.GetProperty("code").GetString() ?? "");
            }
            catch
            {
                // bỏ qua tin nhắn không đúng định dạng
            }
        }

        /// <summary>
        /// Quét xong 1 mã PO (12 ký tự): xóa ô TerminalID trên web, gõ Terminal ID, chọn Shift, chờ AttendanceDate rồi nhấn Login,
        /// chờ trang Operator entry rồi nhập MSNV, chờ #lblPO rồi nhập số PO.
        /// </summary>
        private async Task HandlePoScannedAsync(string po)
        {
            po = po.Trim().ToUpperInvariant();
            if (po.Length != WebAutomation.ScanLength) return;

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
            if (_busy) return;

            _busy = true;
            lblCurrentPO.Text = po;
            SetStatus($"Đã quét PO: {po} — đang điền Terminal ID, chọn Shift, Login, nhập MSNV và PO...");
            try
            {
                webView.Focus();   // để phím gõ tự động đi vào trang web
                var error = await _automation.FillLoginAsync(terminalId, msnv, po);
                if (error is null)
                    SetStatus($"PO: {po} | Đã Login (Terminal \"{terminalId}\"), nhập MSNV \"{msnv}\" và nhập PO  ({DateTime.Now:HH:mm:ss})");
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

        // ===== Terminal ID =====

        private void TxtTerminalId_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            SaveTerminalId();
            txtMsnv.Focus();  // nhập xong Terminal ID → chuyển sang ô MSNV
        }

        private void TxtTerminalId_LostFocus(object sender, RoutedEventArgs e) => SaveTerminalId();

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

        private void TxtMsnv_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            SaveText(MsnvFile, txtMsnv.Text, "MSNV");
            txtPO.Focus();  // nhập xong MSNV → chuyển sang ô quét PO
        }

        private void TxtMsnv_LostFocus(object sender, RoutedEventArgs e) => SaveText(MsnvFile, txtMsnv.Text, "MSNV");

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
                case Key.F2:            // đưa con trỏ về ô quét PO
                    txtPO.Focus();
                    txtPO.SelectAll();
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
            var alert = new AlertWindow(title, message) { Owner = this };
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
