using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace auto_scan_crack
{
    /// <summary>
    /// Tab trình duyệt kiểu Edge: mở app có sẵn 3 tab, nút "+" mở thêm tab, nút "×" đóng tab.
    /// Mỗi tab có WebView2 và profile riêng (cookie/phiên đăng nhập độc lập, như các trình duyệt khác nhau)
    /// nên đăng nhập ở tab này không ảnh hưởng tab kia.
    /// Quét PO → tự động click/nhập trên tab đang được chọn.
    /// </summary>
    public partial class MainWindow
    {
        private const int InitialTabCount = 3;
        private const int MaxTabCount = 10;

        private sealed class BrowserTab
        {
            /// <summary>Số tab = số profile: tab 1 dùng profile mặc định, tab N dùng profile "TabN".</summary>
            public required int Number { get; init; }
            public required TabItem Item { get; init; }
            public required WebView2 View { get; init; }
            public required Button CloseButton { get; init; }
            public required TextBlock TitleBlock { get; init; }

            /// <summary>Tên người dùng đặt (null = tên mặc định "Tab N").</summary>
            public string? CustomName { get; set; }
            public string Title => string.IsNullOrWhiteSpace(CustomName) ? $"Tab {Number}" : CustomName!;

            public WebAutomation? Automation { get; set; }
            public bool Navigating { get; set; }
            public string Status { get; set; } = "Đang khởi tạo...";
            public string Url { get; set; } = "";
        }

        private readonly List<BrowserTab> _tabs = new();
        private CoreWebView2Environment? _webEnv;

        /// <summary>Tab đang chạy tự động (null nếu không chạy) — khóa chuyển/đóng/mở tab trong lúc chạy.</summary>
        private BrowserTab? _runningTab;

        private BrowserTab? SelectedTab => (tabBrowsers.SelectedItem as TabItem)?.Tag as BrowserTab;

        /// <summary>Khởi tạo môi trường WebView2 rồi mở sẵn 3 tab.</summary>
        private async Task InitBrowserTabsAsync()
        {
            try
            {
                _webEnv = await CoreWebView2Environment.CreateAsync(null, UserDataFolder);
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

            LoadTabNames();
            for (int i = 0; i < InitialTabCount; i++)
                await AddTabAsync(select: i == 0);
        }

        /// <summary>Thêm 1 tab mới, lấy số tab nhỏ nhất đang trống (để dùng lại profile cũ).</summary>
        private async Task AddTabAsync(bool select)
        {
            if (_webEnv is null) return;

            int number = 1;
            while (_tabs.Any(t => t.Number == number)) number++;

            var view = new WebView2 { Visibility = Visibility.Collapsed };
            var close = new Button
            {
                Content = "×",
                FontSize = 14,
                Width = 22,
                Height = 22,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Focusable = false,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Đóng tab",
                VerticalAlignment = VerticalAlignment.Center,
            };
            var titleBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(titleBlock);
            header.Children.Add(close);

            var item = new TabItem { Header = header, ToolTip = "Nháy đúp hoặc chuột phải để đổi tên tab" };
            var tab = new BrowserTab
            {
                Number = number, Item = item, View = view, CloseButton = close, TitleBlock = titleBlock,
                CustomName = _tabNames.GetValueOrDefault(number),
            };
            item.Tag = tab;
            UpdateTabHeader(tab);
            close.Click += (_, _) => CloseTab(tab);

            // Đổi tên tab: nháy đúp vào tên tab, hoặc chuột phải → "Đổi tên tab"
            item.MouseDoubleClick += (_, e) =>
            {
                if (IsInside(e.OriginalSource as DependencyObject, close)) return;
                e.Handled = true;
                BeginRenameTab(tab);
            };
            var menuRename = new MenuItem { Header = "Đổi tên tab" };
            menuRename.Click += (_, _) => BeginRenameTab(tab);
            var menuReset = new MenuItem { Header = "Đặt lại tên mặc định" };
            menuReset.Click += (_, _) => SetTabName(tab, null);
            var menuClose = new MenuItem { Header = "Đóng tab" };
            menuClose.Click += (_, _) => CloseTab(tab);
            item.ContextMenu = new ContextMenu { Items = { menuRename, menuReset, new Separator(), menuClose } };

            // Giữ thứ tự tab theo số
            int index = _tabs.Count(t => t.Number < number);
            _tabs.Insert(index, tab);
            tabBrowsers.Items.Insert(index, item);
            browserHost.Children.Add(view);
            UpdateCloseButtons();

            if (select) tabBrowsers.SelectedItem = item;

            try
            {
                await InitTabAsync(tab, _webEnv);
            }
            catch (Exception ex)
            {
                SetTabStatus(tab, "Lỗi khởi tạo trình duyệt: " + ex.Message);
            }
        }

        private async Task InitTabAsync(BrowserTab tab, CoreWebView2Environment env)
        {
            // Tab 1 dùng profile mặc định (giữ phiên đăng nhập cũ), tab khác dùng profile riêng
            if (tab.Number == 1)
                await tab.View.EnsureCoreWebView2Async(env);
            else
            {
                var options = env.CreateCoreWebView2ControllerOptions();
                options.ProfileName = $"Tab{tab.Number}";
                await tab.View.EnsureCoreWebView2Async(env, options);
            }

            if (!_tabs.Contains(tab)) return;   // tab đã bị đóng trong lúc khởi tạo

            var core = tab.View.CoreWebView2;
            core.Settings.AreDevToolsEnabled = true;          // tắt khi phát hành nếu cần
            core.Settings.IsStatusBarEnabled = false;

            tab.Automation = new WebAutomation(core, () => tab.Navigating);

            // Bắt mã quét khi con trỏ đang nằm trong trang web
            await core.AddScriptToExecuteOnDocumentCreatedAsync(WebAutomation.ScanHookScript);
            core.WebMessageReceived += Core_WebMessageReceived;

            core.NavigationStarting += (_, args) =>
            {
                tab.Navigating = true;
                SetTabUrl(tab, args.Uri);
                if (_runningTab != tab) SetTabStatus(tab, "Đang tải...");
            };
            core.NavigationCompleted += (_, args) =>
            {
                tab.Navigating = false;
                SetTabUrl(tab, core.Source);
                if (_runningTab == tab) return;
                SetTabStatus(tab, args.IsSuccess
                    ? "Sẵn sàng"
                    : $"Không tải được trang ({args.WebErrorStatus}). Kiểm tra kết nối tới máy chủ.");
            };
            // Link mở cửa sổ mới (target=_blank, window.open) → mở ngay trong tab hiện tại
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                core.Navigate(args.Uri);
            };

            core.Navigate(HomeUrl);
        }

        private async void BtnAddTab_Click(object sender, RoutedEventArgs e)
        {
            if (_runningTab is not null)
            {
                SetStatus($"Đang chạy PO trên {_runningTab.Title}, chờ xong rồi mới mở tab mới.");
                return;
            }
            if (_tabs.Count >= MaxTabCount)
            {
                SetStatus($"Chỉ mở tối đa {MaxTabCount} tab.");
                return;
            }
            await AddTabAsync(select: true);
        }

        private void CloseTab(BrowserTab tab)
        {
            if (_runningTab is not null)
            {
                SetStatus($"Đang chạy PO trên {_runningTab.Title}, chờ xong rồi mới đóng tab.");
                return;
            }
            if (_tabs.Count <= 1 || !_tabs.Contains(tab)) return;   // luôn giữ ít nhất 1 tab
            if (_renamingTab == tab) EndRenameTab(save: false);

            int index = _tabs.IndexOf(tab);
            bool wasSelected = SelectedTab == tab;

            _tabs.Remove(tab);
            tabBrowsers.Items.Remove(tab.Item);
            browserHost.Children.Remove(tab.View);
            tab.View.Dispose();
            UpdateCloseButtons();

            if (wasSelected)
                tabBrowsers.SelectedIndex = Math.Min(index, _tabs.Count - 1);
        }

        /// <summary>Chỉ còn 1 tab thì ẩn nút × (như Edge không cho đóng tab cuối trong app này).</summary>
        private void UpdateCloseButtons()
        {
            foreach (var t in _tabs)
                t.CloseButton.Visibility = _tabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Trạng thái riêng của 1 tab; chỉ hiện lên thanh trạng thái nếu tab đó đang được chọn.</summary>
        private void SetTabStatus(BrowserTab tab, string text)
        {
            tab.Status = text;
            if (SelectedTab == tab) lblStatus.Text = text;
        }

        private void SetTabUrl(BrowserTab tab, string url)
        {
            tab.Url = url;
            if (SelectedTab == tab) lblUrl.Text = $"[{tab.Title}] {url}";
        }

        private void TabBrowsers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != tabBrowsers) return;   // bỏ qua sự kiện nổi bọt từ control con
            var selected = SelectedTab;
            foreach (var t in _tabs)
                t.View.Visibility = t == selected ? Visibility.Visible : Visibility.Collapsed;
            if (selected is null) return;
            lblStatus.Text = selected.Status;
            lblUrl.Text = $"[{selected.Title}] {selected.Url}";
        }

        /// <summary>Trong lúc tự động chạy trên 1 tab thì khóa các tab khác để không chuyển tab giữa chừng.</summary>
        private void LockTabs(BrowserTab? running)
        {
            if (running is not null && _renamingTab is not null) EndRenameTab(save: true);
            _runningTab = running;
            foreach (var t in _tabs)
            {
                t.Item.IsEnabled = running is null || t == running;
                t.CloseButton.IsEnabled = running is null;
                UpdateTabHeader(t);
            }
        }

        private void UpdateTabHeader(BrowserTab tab) =>
            tab.TitleBlock.Text = _runningTab == tab ? $"{tab.Title} ▶" : tab.Title;

        // ===== Đổi tên tab =====

        private const int MaxTabNameLength = 30;

        /// <summary>Tên tab đã đặt, lưu theo số tab (tab mở lại cùng số thì giữ tên cũ).</summary>
        private static readonly string TabNamesFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "auto_scan_crack", "tab_names.json");

        private Dictionary<int, string> _tabNames = new();
        private BrowserTab? _renamingTab;
        private TextBox? _renameBox;

        /// <summary>Đang gõ tên tab → các phím đi thẳng vào ô đổi tên, không gom vào bộ đệm quét mã.</summary>
        private bool IsRenamingTab => _renameBox?.IsKeyboardFocusWithin == true;

        private void BeginRenameTab(BrowserTab tab)
        {
            if (_runningTab is not null)
            {
                SetStatus($"Đang chạy PO trên {_runningTab.Title}, chờ xong rồi mới đổi tên tab.");
                return;
            }
            if (_renamingTab is not null) EndRenameTab(save: true);

            var header = (StackPanel)tab.Item.Header;
            var box = new TextBox
            {
                Text = tab.Title,
                MinWidth = 110,
                MaxLength = MaxTabNameLength,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(2, 0, 2, 0),
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; EndRenameTab(save: true); txtPO.Focus(); }
                else if (e.Key == Key.Escape) { e.Handled = true; EndRenameTab(save: false); txtPO.Focus(); }
            };
            box.LostKeyboardFocus += (_, _) =>
            {
                // Hoãn lại để không đổi cây giao diện ngay giữa lúc WPF đang chuyển focus
                if (_renameBox == box)
                    Dispatcher.BeginInvoke(() => { if (_renameBox == box && !box.IsKeyboardFocusWithin) EndRenameTab(save: true); },
                        DispatcherPriority.Background);
            };

            // Ẩn tên, chèn ô nhập ngay sau (không gán đè Children[i] vì WPF báo "Specified index is already in use")
            tab.TitleBlock.Visibility = Visibility.Collapsed;
            header.Children.Insert(header.Children.IndexOf(tab.TitleBlock) + 1, box);
            _renamingTab = tab;
            _renameBox = box;

            // Chờ menu chuột phải đóng hẳn rồi mới đưa con trỏ vào ô (tránh menu trả focus về chỗ cũ)
            Dispatcher.BeginInvoke(() =>
            {
                if (_renameBox != box) return;
                box.Focus();
                box.SelectAll();
            }, DispatcherPriority.ContextIdle);
        }

        private void EndRenameTab(bool save)
        {
            var tab = _renamingTab;
            var box = _renameBox;
            if (tab is null || box is null) return;
            _renamingTab = null;
            _renameBox = null;

            ((StackPanel)tab.Item.Header).Children.Remove(box);
            tab.TitleBlock.Visibility = Visibility.Visible;

            if (save) SetTabName(tab, box.Text);
        }

        /// <summary>Đặt tên tab (rỗng / null = về tên mặc định "Tab N") và lưu lại.</summary>
        private void SetTabName(BrowserTab tab, string? name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name == $"Tab {tab.Number}") name = null;

            tab.CustomName = name;
            if (name is null) _tabNames.Remove(tab.Number);
            else _tabNames[tab.Number] = name;
            SaveTabNames();

            UpdateTabHeader(tab);
            if (SelectedTab == tab) lblUrl.Text = $"[{tab.Title}] {tab.Url}";
        }

        private void LoadTabNames()
        {
            try
            {
                if (File.Exists(TabNamesFile))
                    _tabNames = JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(TabNamesFile)) ?? new();
            }
            catch
            {
                _tabNames = new();   // file hỏng → dùng tên mặc định
            }
        }

        private void SaveTabNames()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TabNamesFile)!);
                File.WriteAllText(TabNamesFile, JsonSerializer.Serialize(_tabNames));
            }
            catch (Exception ex)
            {
                SetStatus("Không lưu được tên tab: " + ex.Message);
            }
        }

        private static bool IsInside(DependencyObject? element, DependencyObject container)
        {
            for (var d = element; d is not null;
                 d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d == container) return true;
            return false;
        }
    }
}
