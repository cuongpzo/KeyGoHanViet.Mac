using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using KeyGoHanViet.Core;
using KeyGoHanViet.Mac.Interop;

namespace KeyGoHanViet.Mac.UI;

/// <summary>Một dòng trong bảng gõ tắt.</summary>
public sealed class MacroRow
{
    public string Key { get; set; } = "";
    public string Values { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>Một dòng trong bảng "chế độ theo từng ứng dụng" của thẻ Chung.</summary>
public sealed class AppModeRow
{
    public string Process { get; init; } = "";
    public string Name { get; init; } = "";
    public string Mode { get; init; } = "";
    public string LastSeen { get; init; } = "";
}

/// <summary>Một dòng kết quả trong ô tra thử của tab Từ điển.</summary>
public sealed class LookupRow
{
    public string Index { get; init; } = "";
    public string Text { get; init; } = "";
    public string Tier { get; init; } = "";
    public string Weight { get; init; } = "";
    public string Usage { get; init; } = "";
    public string Hint { get; init; } = "";
}

/// <summary>
/// Cửa sổ tuỳ chọn: bật/tắt tính năng, đổi bộ gõ, sửa gõ tắt, tra thử từ điển.
/// Bản macOS của <c>UI\OptionsForm.cs</c> bên Windows, giữ nguyên bảy thẻ và
/// từng mục trong đó.
/// </summary>
public sealed class OptionsWindow : Window
{
    private readonly App _app;
    private AppSettings _draft;
    private bool _loading;

    private readonly TextBlock _status = new() { Margin = new Thickness(12, 4, 12, 6) };

    /// <summary>
    /// Thứ tự các mục trong ô chọn phím chuyển. Tách riêng khỏi thứ tự khai báo
    /// của enum để sau này thêm kiểu mới không làm lệch lựa chọn đã lưu.
    /// </summary>
    private static readonly ToggleKeyKind[] ToggleOrder =
    [
        ToggleKeyKind.ShiftTap,
        ToggleKeyKind.ControlShift,
        ToggleKeyKind.AltZ,
        ToggleKeyKind.ControlSpace,
        ToggleKeyKind.Custom,
        ToggleKeyKind.None,
    ];

    // --- thẻ Chung
    private CheckBox _cbEnabled = null!;
    private RadioButton _rbHan = null!;
    private RadioButton _rbLatin = null!;
    private ComboBox _cbToggleKey = null!;
    private HotkeyBox _txtHotkey = null!;
    private TextBlock _lblHotkey = null!;
    private ComboBox _cbOutput = null!;
    private CheckBox _cbStartup = null!;
    private CheckBox _cbTray = null!;
    private CheckBox _cbPerApp = null!;
    private DataGrid _gridApps = null!;
    private TextBlock _lblActiveApp = null!;
    private TextBlock _lblConfigPath = null!;
    private readonly ObservableCollection<AppModeRow> _appRows = [];

    // --- thẻ Kiểu gõ
    private CheckBox _cbViet = null!;
    private HotkeyBox _txtClipboardKey = null!;
    private HotkeyBox _txtSelectionKey = null!;
    private CheckBox _cbHanToViet = null!;
    private HotkeyBox _txtHanToVietKey = null!;
    private CheckBox _cbPlain = null!;
    private CheckBox _cbComposite = null!;
    private CheckBox _cbMacros = null!;
    private CheckBox _cbAbbrevDict = null!;
    private CheckBox _cbPrefix = null!;
    private NumericUpDown _numMinPrefix = null!;
    private CheckBox _cbLearn = null!;
    private CheckBox _cbFreeOrder = null!;
    private CheckBox _cbSupplement = null!;
    private CheckBox _cbModernTone = null!;
    private CheckBox _cbAppendSpace = null!;
    private CheckBox _cbDigit = null!;

    // --- thẻ Giao diện
    private NumericUpDown _numMaxCand = null!;
    private NumericUpDown _numPool = null!;
    private ComboBox _cbHanFont = null!;
    private NumericUpDown _numHanSize = null!;
    private CheckBox _cbShowHints = null!;
    private CheckBox _cbShowPreedit = null!;
    private CheckBox _cbDark = null!;

    // --- thẻ Từ điển
    private TextBox _txtMainDict = null!;
    private TextBox _txtUserDict = null!;
    private TextBlock _lblDictStats = null!;
    private TextBox _txtQuery = null!;
    private TextBlock _lblPreedit = null!;
    private readonly ObservableCollection<LookupRow> _results = [];

    // --- thẻ Gõ tắt
    private readonly ObservableCollection<MacroRow> _macroRows = [];

    // --- thẻ Thử gõ
    private TextBox _txtPad = null!;

    public OptionsWindow(App app)
    {
        _app = app;
        _draft = app.Settings.Clone();

        Title = $"KeyGo Hán Việt {AppInfo.Version} — Tuỳ chọn";
        Icon = MacTrayIcons.App;
        Width = 920;
        Height = 660;
        MinWidth = 780;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildLayout();
        LoadFromSettings();

        _app.Ime.Committed += OnCommitted;
    }

    // ------------------------------------------------------------ bố cục

    private void BuildLayout()
    {
        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Chung", Content = BuildGeneralTab() },
                new TabItem { Header = "Kiểu gõ", Content = BuildTypingTab() },
                new TabItem { Header = "Giao diện", Content = BuildAppearanceTab() },
                new TabItem { Header = "Từ điển", Content = BuildDictionaryTab() },
                new TabItem { Header = "Gõ tắt", Content = BuildMacroTab() },
                new TabItem { Header = "Thử gõ", Content = BuildTestTab() },
                new TabItem { Header = "Giới thiệu", Content = BuildAboutTab() },
            },
        };

        var btnReset = Btn("Khôi phục mặc định", async () => await ResetDefaults());
        var btnClose = Btn("Đóng", Close);
        var btnApply = Btn("Áp dụng", Apply);
        var btnOk = Btn("Lưu & đóng", () => { Apply(); Close(); });
        btnOk.IsDefault = true;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(12, 8, 12, 4),
            Children = { btnReset, btnOk, btnApply, btnClose },
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
        };
        grid.Children.Add(tabs);

        Grid.SetRow(buttons, 1);
        grid.Children.Add(buttons);

        Grid.SetRow(_status, 2);
        grid.Children.Add(_status);

        Content = grid;
    }

    private static Button Btn(string text, Action onClick)
    {
        var b = new Button { Content = text, MinWidth = 100 };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Khung cuộn được cho mỗi thẻ, lề giống nhau ở mọi thẻ.</summary>
    private static ScrollViewer Page(StackPanel content) => new()
    {
        Content = new StackPanel { Margin = new Thickness(20), Spacing = 8, Children = { content } },
    };

    private static StackPanel Stack(params Control[] children)
    {
        var p = new StackPanel { Spacing = 8 };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 14, 0, 2),
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Opacity = 0.65,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 10, 0, 0),
    };

    /// <summary>Một dòng "nhãn bên trái, điều khiển bên phải".</summary>
    private static Control Row(string caption, Control control)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = caption,
                    Width = 190,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                control,
            },
        };
        return panel;
    }

    // --------------------------------------------------------- thẻ: Chung

    private Control BuildGeneralTab()
    {
        _cbEnabled = new CheckBox { Content = "Bật bộ gõ Hán Nôm" };

        _rbHan = new RadioButton { Content = "K — Gõ Hán Nôm", GroupName = "mode" };
        _rbLatin = new RadioButton { Content = "Q — Tắt, gõ Latin (tiếng Anh)", GroupName = "mode" };

        _cbToggleKey = new ComboBox
        {
            Width = 300,
            ItemsSource = new[]
            {
                "Shift (nhấn nhanh, đúng đặc tả)",
                "Ctrl + Shift",
                "⌥ Option + Z",
                "Ctrl + Space",
                "Tự đặt tổ hợp phím…",
                "Không dùng phím chuyển",
            },
        };
        _cbToggleKey.SelectionChanged += (_, _) => UpdateHotkeyEnabled();

        _txtHotkey = new HotkeyBox { Width = 300 };
        _lblHotkey = new TextBlock
        {
            Text = "Tổ hợp tự đặt:",
            Width = 190,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var hotkeyRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { _lblHotkey, _txtHotkey },
        };

        _cbOutput = new ComboBox
        {
            Width = 300,
            ItemsSource = new[]
            {
                "Sự kiện phím Unicode (khuyên dùng)",
                "Clipboard + ⌘V (dự phòng)",
            },
        };

        _cbStartup = new CheckBox { Content = "Chạy khi đăng nhập macOS" };
        _cbTray = new CheckBox { Content = "Thu về thanh trình đơn khi đóng cửa sổ này" };

        _cbPerApp = new CheckBox { Content = "Mỗi ứng dụng nhớ chế độ K/Q của riêng nó" };
        _cbPerApp.IsCheckedChanged += (_, _) => UpdatePerAppEnabled();

        _lblActiveApp = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _lblConfigPath = new TextBlock { Opacity = 0.65, TextWrapping = TextWrapping.Wrap };

        _gridApps = new DataGrid
        {
            ItemsSource = _appRows,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            Height = 190,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Columns =
            {
                new DataGridTextColumn { Header = "Ứng dụng", Width = new DataGridLength(200), Binding = new Binding(nameof(AppModeRow.Name)) },
                new DataGridTextColumn { Header = "Định danh", Width = new DataGridLength(230), Binding = new Binding(nameof(AppModeRow.Process)) },
                new DataGridTextColumn { Header = "Chế độ", Width = new DataGridLength(110), Binding = new Binding(nameof(AppModeRow.Mode)) },
                new DataGridTextColumn { Header = "Lần cuối dùng", Width = new DataGridLength(120), Binding = new Binding(nameof(AppModeRow.LastSeen)) },
            },
        };

        var appButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Btn("Đổi K ⇄ Q", ToggleSelectedApp),
                Btn("Xoá khỏi danh sách", ForgetSelectedApp),
                Btn("Làm mới", LoadAppList),
                Btn("Mở config.json", OpenConfigFile),
            },
        };

        return Page(Stack(
            _cbEnabled,
            Section("Chế độ hiện tại"),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { _rbHan, _rbLatin } },
            Row("Phím chuyển K ⇄ Q:", _cbToggleKey),
            hotkeyRow,
            Note("Bấm vào ô trên rồi bấm tổ hợp muốn dùng (ví dụ Ctrl + ⌥ + H).\n" +
                 "Nhấn Backspace để xoá. Nên tránh tổ hợp mà macOS hoặc ứng dụng khác đã dùng — " +
                 "Ctrl + Space chẳng hạn, mặc định macOS đã dành cho việc đổi nguồn nhập liệu."),
            Row("Cách xuất chữ:", _cbOutput),
            Section("Khởi động"),
            _cbStartup,
            _cbTray,
            Section("Chế độ riêng cho từng ứng dụng"),
            _cbPerApp,
            Note("Bật chữ Hán trong Safari thì chỉ Safari gõ ra chữ Hán; chuyển sang cửa sổ khác, bộ gõ " +
                 "tự về Latin (Q). Sang Pages rồi bật chữ Hán lần nữa thì cả hai cùng nhớ, các ứng dụng " +
                 "còn lại vẫn gõ Latin. Tắt ô này thì chế độ dùng chung cho toàn hệ thống như trước."),
            _lblActiveApp,
            _gridApps,
            appButtons,
            _lblConfigPath,
            Note("Biểu tượng ở thanh trình đơn cho biết chế độ hiện tại:\n" +
                 "    K  — bộ gõ đang bật, gõ ra chữ Hán Nôm\n" +
                 "    Q  — bộ gõ đã tắt, gõ Latin bình thường\n\n" +
                 "Bấm vào biểu tượng để mở trình đơn; mục đầu tiên đổi qua lại giữa K và Q.\n" +
                 "Bộ gõ cần quyền Trợ năng trong Cài đặt Hệ thống ▸ Quyền riêng tư & Bảo mật ▸ Trợ năng. " +
                 "Không có quyền đó thì macOS không cho đọc bàn phím và bộ gõ sẽ nằm im.")));
    }

    // -------------------------------------------------------- thẻ: Kiểu gõ

    private Control BuildTypingTab()
    {
        _cbPlain = new CheckBox { Content = "Gõ không dấu hoàn toàn (vietnam → 越南)" };
        _cbComposite = new CheckBox { Content = "Ghép nhiều cụm từ (xahooichunghia → 社會主義)" };
        _cbMacros = new CheckBox { Content = "Dùng bảng gõ tắt của người dùng" };
        _cbAbbrevDict = new CheckBox { Content = "Khớp viết tắt chữ cái đầu suy ra từ từ điển" };
        _cbFreeOrder = new CheckBox { Content = "Gõ TELEX tự do thứ tự (dofod, ddofo → đồ)" };
        _cbSupplement = new CheckBox { Content = "Nạp thêm âm đọc từ ChinesePhienAmWords.txt" };
        _cbPrefix = new CheckBox { Content = "Gợi ý ngay khi đang gõ dở" };
        _numMinPrefix = new NumericUpDown { Minimum = 1, Maximum = 10, Increment = 1, Width = 110 };

        _cbLearn = new CheckBox { Content = "Ghi nhớ lựa chọn để lần sau xếp lên đầu" };
        _cbModernTone = new CheckBox { Content = "Đặt dấu thanh kiểu mới (nghĩa thay vì nghiã)" };
        _cbAppendSpace = new CheckBox { Content = "Chèn thêm dấu cách sau khi xác nhận" };
        _cbDigit = new CheckBox { Content = "Chọn nhanh ứng viên bằng phím số 1–9" };

        _cbViet = new CheckBox
        {
            Content = "Tra được bằng chính âm đọc (hán việt từ điển trích dẫn → 漢越辭典摘引)",
        };
        _cbViet.IsCheckedChanged += (_, _) => UpdateVietEnabled();

        _txtClipboardKey = new HotkeyBox { Width = 300 };
        _txtSelectionKey = new HotkeyBox { Width = 300 };

        _cbHanToViet = new CheckBox
        {
            Content = "Đọc âm Quốc ngữ của đoạn chữ Hán đang bôi đen (滴答滴答答 → Tích đáp tích đáp đáp)",
        };
        _cbHanToViet.IsCheckedChanged += (_, _) => UpdateHanToVietEnabled();

        _txtHanToVietKey = new HotkeyBox { Width = 300 };

        return Page(Stack(
            Section("Nguồn gợi ý"),
            _cbPlain, _cbComposite, _cbMacros, _cbAbbrevDict, _cbFreeOrder, _cbSupplement, _cbPrefix,
            Row("Số ký tự tối thiểu để gợi ý:", _numMinPrefix),
            Section("Hành vi"),
            _cbLearn, _cbModernTone, _cbAppendSpace, _cbDigit,
            Section("Tra bằng chữ Quốc ngữ có dấu"),
            _cbViet,
            Row("Tra nội dung clipboard:", _txtClipboardKey),
            Row("Chuyển đoạn đang bôi đen:", _txtSelectionKey),
            Note("Bấm vào ô rồi bấm tổ hợp muốn dùng; Backspace để xoá, khi đó phím tắt tắt hẳn.\n" +
                 "⌘V vẫn dán bình thường — bộ gõ cố ý không chiếm tổ hợp đó.\n" +
                 "Chữ có dấu do bộ gõ khác sinh ra cũng vào thẳng khung gợi ý.\n" +
                 "Việc chuyển đoạn bôi đen cần bơm ⌘C tới ứng dụng đích, nên phải có quyền Trợ năng."),
            Section("Xem nhanh âm đọc (chiều ngược lại)"),
            _cbHanToViet,
            Row("Phím tắt xem âm đọc:", _txtHanToVietKey),
            Note("Kết quả hiện trong một cửa sổ nhỏ, sao chép được, tự tắt khi bấm ra ngoài hoặc nhấn Esc.\n" +
                 "Không sửa gì trong ứng dụng đích, và chạy được cả khi đang ở chế độ Latin (Q).\n" +
                 "Chữ nào có nhiều âm thì bấm vào chữ đó trong cửa sổ để đổi sang âm khác.\n" +
                 "Cũng cần quyền Trợ năng vì phải bơm ⌘C để lấy đoạn đang bôi đen."),
            Note("Quy ước TELEX của bộ từ điển hannomPS:\n" +
                 "    s = sắc    f = huyền    r = hỏi    x = ngã    j = nặng    z = ngang (bắt buộc)\n" +
                 "    aa = â    aw = ă    ee = ê    oo = ô    ow = ơ    uw = ư    dd = đ\n\n" +
                 "Phím chức năng khi khung gợi ý đang hiện:\n" +
                 "    ↑ ↓ / Tab       duyệt danh sách\n" +
                 "    Space            xác nhận chữ Hán Nôm đang chọn\n" +
                 "    Enter            huỷ gợi ý, xuất nguyên chuỗi Latin\n" +
                 "    Esc              bỏ chuỗi đang gõ\n" +
                 "    1–9              chọn nhanh theo số thứ tự")));
    }

    // ------------------------------------------------------ thẻ: Giao diện

    private Control BuildAppearanceTab()
    {
        _numMaxCand = new NumericUpDown { Minimum = 1, Maximum = 20, Increment = 1, Width = 110 };
        _numPool = new NumericUpDown { Minimum = 20, Maximum = 5000, Increment = 50, Width = 130 };

        _cbHanFont = new ComboBox { Width = 320, ItemsSource = SystemFontNames() };

        _numHanSize = new NumericUpDown { Minimum = 10, Maximum = 48, Increment = 1, Width = 110 };

        _cbShowHints = new CheckBox { Content = "Hiện mã TELEX bên cạnh mỗi ứng viên" };
        _cbShowPreedit = new CheckBox { Content = "Hiện dòng Quốc ngữ có dấu phía trên danh sách" };
        _cbDark = new CheckBox { Content = "Giao diện tối cho khung gợi ý" };

        return Page(Stack(
            Row("Số ứng viên hiển thị:", _numMaxCand),
            Row("Số ứng viên giữ lại (lật bằng Tab):", _numPool),
            Note("Khung gợi ý chỉ hiện được ngần ấy dòng một lúc, nhưng một âm như \"tuwz\" (tư) có tới " +
                 "hàng chục chữ đồng âm. Nhấn Tab để lật sang nhóm kế tiếp, Shift + Tab để lật ngược; " +
                 "phím ↑ ↓ vẫn nhích từng chữ và tự sang trang khi đi hết dòng cuối."),
            Row("Phông chữ Hán Nôm:", _cbHanFont),
            Row("Cỡ chữ Hán Nôm:", _numHanSize),
            _cbShowHints, _cbShowPreedit, _cbDark,
            Note("Nhiều chữ Nôm nằm ngoài mặt phẳng cơ bản (như 𡨸, 𣛠). Để hiển thị đầy đủ, hãy cài\n" +
                 "phông hỗ trợ CJK Ext-B trở lên: Nom Na Tong, HanaMinB hoặc Han Nom A/B.\n" +
                 "Phông đi kèm macOS không phủ hết khối chữ Nôm, thiếu phông thì một số chữ hiện ô vuông.")));
    }

    private static List<string> SystemFontNames()
    {
        var names = FontManager.Current.SystemFonts
                               .Select(f => f.Name)
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .OrderBy(n => n, StringComparer.CurrentCulture)
                               .ToList();

        // Phông chữ Nôm hay dùng nhưng máy có thể chưa cài: vẫn cho chọn sẵn để
        // người dùng cài sau mà không phải quay lại đây gõ tay.
        foreach (string wanted in new[] { "Nom Na Tong", "HanaMinB", "Han Nom A", "Han Nom B" })
            if (!names.Contains(wanted, StringComparer.OrdinalIgnoreCase))
                names.Insert(0, wanted);

        return names;
    }

    // ------------------------------------------------------- thẻ: Từ điển

    private Control BuildDictionaryTab()
    {
        _txtMainDict = new TextBox { Width = 460 };
        _txtUserDict = new TextBox { Width = 460 };

        var browseMain = Btn("Chọn…", async () => await Browse(_txtMainDict));
        browseMain.MinWidth = 80;
        var browseUser = Btn("Chọn…", async () => await Browse(_txtUserDict));
        browseUser.MinWidth = 80;

        _lblDictStats = new TextBlock { Opacity = 0.65, TextWrapping = TextWrapping.Wrap };

        _txtQuery = new TextBox { Width = 460, Watermark = "Gõ thử: vieetjnamz, xahooichunghia, hán việt từ điển trích dẫn…" };
        _txtQuery.TextChanged += (_, _) => RunQuery();

        _lblPreedit = new TextBlock { FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 4) };

        var results = new DataGrid
        {
            ItemsSource = _results,
            IsReadOnly = true,
            AutoGenerateColumns = false,
            Height = 260,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
        };
        AddColumn(results, "#", nameof(LookupRow.Index), 40);
        AddColumn(results, "Chữ Hán Nôm", nameof(LookupRow.Text), 180);
        AddColumn(results, "Nguồn khớp", nameof(LookupRow.Tier), 110);
        AddColumn(results, "Trọng số", nameof(LookupRow.Weight), 90);
        AddColumn(results, "Đã dùng", nameof(LookupRow.Usage), 80);
        AddColumn(results, "Mã TELEX", nameof(LookupRow.Hint), 260);

        return Page(Stack(
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children = { new TextBlock { Text = "Từ điển chính:", Width = 140, VerticalAlignment = VerticalAlignment.Center }, _txtMainDict, browseMain },
            },
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children = { new TextBlock { Text = "Từ điển bổ sung:", Width = 140, VerticalAlignment = VerticalAlignment.Center }, _txtUserDict, browseUser },
            },
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0),
                Children =
                {
                    Btn("Nạp lại từ điển", () => { Apply(); _app.ReloadDictionary(); }),
                    Btn("Mở tệp từ điển bổ sung", () => OpenInEditor(AppPaths.ResolveUserDict(_txtUserDict.Text ?? ""))),
                    Btn("Xoá lịch sử học", () => { _app.Usage.Clear(); SetStatus("Đã xoá lịch sử ghi nhớ lựa chọn."); }),
                },
            },
            _lblDictStats,
            Section("Tra thử"),
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children = { new TextBlock { Text = "Chuỗi gõ:", Width = 140, VerticalAlignment = VerticalAlignment.Center }, _txtQuery },
            },
            _lblPreedit,
            results));
    }

    private static void AddColumn(DataGrid grid, string header, string property, double width, bool readOnly = true)
    {
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(property) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay },
            Width = new DataGridLength(width),
            IsReadOnly = readOnly,
        });
    }

    // -------------------------------------------------------- thẻ: Gõ tắt

    private Control BuildMacroTab()
    {
        var grid = new DataGrid
        {
            ItemsSource = _macroRows,
            AutoGenerateColumns = false,
            IsReadOnly = false,
            Height = 380,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
        };
        AddColumn(grid, "Chuỗi gõ tắt", nameof(MacroRow.Key), 200, readOnly: false);
        AddColumn(grid, "Kết quả (ngăn cách bằng |)", nameof(MacroRow.Values), 380, readOnly: false);
        AddColumn(grid, "Ghi chú", nameof(MacroRow.Note), 260, readOnly: false);

        return Page(Stack(
            new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Mỗi dòng là một chuỗi gõ tắt. Cột “Kết quả” có thể chứa nhiều lựa chọn, ngăn cách bằng dấu |\n" +
                       "Ví dụ: chxhcnvn  →  共和社會主義越南",
            },
            grid,
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children =
                {
                    // Bảng của Avalonia không có dòng trống cuối để gõ thêm như
                    // DataGridView của WinForms, nên thêm dòng bằng nút riêng.
                    Btn("Thêm dòng", () => _macroRows.Add(new MacroRow())),
                    Btn("Xoá dòng đang chọn", () =>
                    {
                        if (grid.SelectedItem is MacroRow row) _macroRows.Remove(row);
                    }),
                    Btn("Lưu bảng gõ tắt", SaveMacros),
                    Btn("Nạp lại", LoadMacrosToGrid),
                    Btn("Mở macros.json", () => OpenInEditor(AppPaths.MacrosFile)),
                },
            }));
    }

    // ------------------------------------------------------- thẻ: Thử gõ

    private Control BuildTestTab()
    {
        _txtPad = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 400,
            FontSize = 20,
        };

        return Page(Stack(
            new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Bấm vào ô dưới rồi gõ thử. Bộ gõ hoạt động y như trong mọi ứng dụng khác.\n" +
                       "Thử: vieetjnamz [Space]   ·   xahooichunghia [Space]   ·   chxhcnvn [Space]",
            },
            _txtPad,
            Btn("Xoá ô thử", () => _txtPad.Text = "")));
    }

    private Control BuildAboutTab() => Page(Stack(new TextBlock
    {
        TextWrapping = TextWrapping.Wrap,
        Text =
            "KeyGo Hán Việt\n" +
            "Bộ gõ chữ Hán Nôm bằng bàn phím Latin — bản macOS\n\n" +
            "Dữ liệu từ điển: hannomPS (Rime) — tác giả Keepout2010.\n" +
            "Bộ Ứng dụng được xây dựng lại: — tác giả Cuongpzo.\n" +
            "Quy tắc chuyển tự lấy từ hannomPS.schema.yaml trong cùng thư mục dự án.\n\n" +
            $"Hồ sơ người dùng: {AppPaths.ProfileDir}\n\n" +
            "Bản macOS dùng chung đúng bộ mã tra cứu và xếp hạng với bản Windows; chỉ lớp\n" +
            "chạm tới bàn phím và giao diện là viết riêng cho từng hệ.\n\n" +
            "Bộ gõ đọc bàn phím qua quyền Trợ năng của macOS, không cài phần mở rộng nhân hệ\n" +
            "điều hành và không gửi dữ liệu ra ngoài máy, hoàn toàn sử dụng trong nội bộ thiết bị.",
    }));

    // ------------------------------------------------------- nạp / lưu

    private void LoadFromSettings()
    {
        _loading = true;

        _cbEnabled.IsChecked = _draft.Enabled;
        _rbHan.IsChecked = _draft.HanNomMode;
        _rbLatin.IsChecked = !_draft.HanNomMode;

        int toggleIndex = Array.IndexOf(ToggleOrder, _draft.ToggleKey);
        _cbToggleKey.SelectedIndex = toggleIndex >= 0 ? toggleIndex : 0;
        _txtHotkey.Hotkey = HotkeySpec.Parse(_draft.CustomToggleKey);
        UpdateHotkeyEnabled();

        _cbOutput.SelectedIndex = (int)_draft.Output;
        _cbStartup.IsChecked = _draft.RunAtStartup || MacStartupRegistration.IsRegistered();
        _cbTray.IsChecked = _draft.MinimizeToTray;

        _cbPerApp.IsChecked = _draft.PerAppMode;
        UpdatePerAppEnabled();
        LoadAppList();

        _cbViet.IsChecked = _draft.EnableVietInput;
        _txtClipboardKey.Hotkey = HotkeySpec.Parse(_draft.ClipboardLookupHotkey);
        _txtSelectionKey.Hotkey = HotkeySpec.Parse(_draft.ConvertSelectionHotkey);
        UpdateVietEnabled();

        _cbHanToViet.IsChecked = _draft.EnableHanToViet;
        _txtHanToVietKey.Hotkey = HotkeySpec.Parse(_draft.HanToVietHotkey);
        UpdateHanToVietEnabled();

        _cbPlain.IsChecked = _draft.EnablePlainInput;
        _cbComposite.IsChecked = _draft.EnableComposite;
        _cbMacros.IsChecked = _draft.EnableMacros;
        _cbAbbrevDict.IsChecked = _draft.EnableAbbrevFromDictionary;
        _cbFreeOrder.IsChecked = _draft.EnableFreeOrderTelex;
        _cbSupplement.IsChecked = _draft.EnableSupplementDictionary;
        _cbPrefix.IsChecked = _draft.EnablePrefixSuggestions;
        _numMinPrefix.Value = _draft.MinPrefixLength;
        _cbLearn.IsChecked = _draft.LearnUsage;
        _cbModernTone.IsChecked = _draft.ModernTonePlacement;
        _cbAppendSpace.IsChecked = _draft.AppendSpaceAfterCommit;
        _cbDigit.IsChecked = _draft.SelectByDigit;

        _numMaxCand.Value = _draft.MaxCandidates;
        _numPool.Value = Math.Clamp(_draft.CandidatePoolSize, 20, 5000);
        _cbHanFont.SelectedItem = _draft.HanFontFamily;
        _numHanSize.Value = (decimal)_draft.HanFontSize;
        _cbShowHints.IsChecked = _draft.ShowHints;
        _cbShowPreedit.IsChecked = _draft.ShowPreedit;
        _cbDark.IsChecked = _draft.DarkTheme;

        _txtMainDict.Text = AppPaths.ResolveMainDict(_draft.MainDictionaryPath);
        _txtUserDict.Text = AppPaths.ResolveUserDict(_draft.UserDictionaryPath);

        LoadMacrosToGrid();
        UpdateDictStats();

        _loading = false;
    }

    private void CollectToDraft()
    {
        _draft.Enabled = _cbEnabled.IsChecked == true;
        _draft.HanNomMode = _rbHan.IsChecked == true;

        int ti = _cbToggleKey.SelectedIndex;
        _draft.ToggleKey = ti >= 0 && ti < ToggleOrder.Length ? ToggleOrder[ti] : ToggleKeyKind.ShiftTap;
        _draft.CustomToggleKey = HotkeySpec.Format(_txtHotkey.Hotkey);

        _draft.Output = (OutputMethod)Math.Max(0, _cbOutput.SelectedIndex);
        _draft.RunAtStartup = _cbStartup.IsChecked == true;
        _draft.MinimizeToTray = _cbTray.IsChecked == true;
        _draft.PerAppMode = _cbPerApp.IsChecked == true;

        _draft.EnableVietInput = _cbViet.IsChecked == true;
        _draft.ClipboardLookupHotkey = HotkeySpec.Format(_txtClipboardKey.Hotkey);
        _draft.ConvertSelectionHotkey = HotkeySpec.Format(_txtSelectionKey.Hotkey);

        _draft.EnableHanToViet = _cbHanToViet.IsChecked == true;
        _draft.HanToVietHotkey = HotkeySpec.Format(_txtHanToVietKey.Hotkey);

        _draft.EnablePlainInput = _cbPlain.IsChecked == true;
        _draft.EnableComposite = _cbComposite.IsChecked == true;
        _draft.EnableMacros = _cbMacros.IsChecked == true;
        _draft.EnableAbbrevFromDictionary = _cbAbbrevDict.IsChecked == true;
        _draft.EnableFreeOrderTelex = _cbFreeOrder.IsChecked == true;
        _draft.EnableSupplementDictionary = _cbSupplement.IsChecked == true;
        _draft.EnablePrefixSuggestions = _cbPrefix.IsChecked == true;
        _draft.MinPrefixLength = (int)(_numMinPrefix.Value ?? 3);
        _draft.LearnUsage = _cbLearn.IsChecked == true;
        _draft.ModernTonePlacement = _cbModernTone.IsChecked == true;
        _draft.AppendSpaceAfterCommit = _cbAppendSpace.IsChecked == true;
        _draft.SelectByDigit = _cbDigit.IsChecked == true;

        _draft.MaxCandidates = (int)(_numMaxCand.Value ?? 9);
        _draft.CandidatePoolSize = (int)(_numPool.Value ?? 500);

        string font = (_cbHanFont.SelectedItem as string ?? "").Trim();
        _draft.HanFontFamily = font.Length > 0 ? font : "Nom Na Tong";

        _draft.HanFontSize = (float)(_numHanSize.Value ?? 18m);
        _draft.ShowHints = _cbShowHints.IsChecked == true;
        _draft.ShowPreedit = _cbShowPreedit.IsChecked == true;
        _draft.DarkTheme = _cbDark.IsChecked == true;

        _draft.MainDictionaryPath = (_txtMainDict.Text ?? "").Trim();
        _draft.UserDictionaryPath = (_txtUserDict.Text ?? "").Trim();
    }

    private void Apply()
    {
        CollectToDraft();

        bool dictChanged =
            !string.Equals(_draft.MainDictionaryPath, AppPaths.ResolveMainDict(_app.Settings.MainDictionaryPath), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_draft.UserDictionaryPath, AppPaths.ResolveUserDict(_app.Settings.UserDictionaryPath), StringComparison.OrdinalIgnoreCase) ||
            _draft.EnableSupplementDictionary != _app.Settings.EnableSupplementDictionary;

        _app.ApplySettings(_draft, dictChanged);
        _draft = _app.Settings.Clone();

        SetStatus("Đã lưu tuỳ chọn lúc " + DateTime.Now.ToString("HH:mm:ss"));
        RunQuery();
    }

    // --------------------------------------- danh sách ứng dụng (config.json)

    /// <summary>
    /// Hiện lại danh sách ứng dụng và chế độ của từng cái.
    ///
    /// Đọc thẳng từ bảng nhớ đang chạy chứ không đọc lại tệp: bảng nhớ mới là
    /// bản đang có hiệu lực, còn tệp chỉ được ghi khi có gì đó thật sự đổi.
    /// </summary>
    private void LoadAppList()
    {
        if (_gridApps is null) return;

        var store = _app.AppModes;
        string? keep = SelectedAppKey;

        _appRows.Clear();

        foreach (var entry in store.Items)
            _appRows.Add(new AppModeRow
            {
                Process = entry.Process,
                Name = entry.Name.Length > 0 ? entry.Name : entry.Process,
                Mode = entry.HanNom ? "K — Hán Nôm" : "Q — Latin",
                LastSeen = entry.LastSeen == default ? "—" : entry.LastSeen.ToString("dd/MM HH:mm"),
            });

        if (keep is { Length: > 0 })
            _gridApps.SelectedItem = _appRows.FirstOrDefault(
                r => string.Equals(r.Process, keep, StringComparison.OrdinalIgnoreCase));

        UpdateActiveAppLabel();
        _lblConfigPath.Text = "Danh sách được ghi ở: " + store.FilePath;
    }

    private void UpdateActiveAppLabel()
    {
        if (_lblActiveApp is null) return;

        string key = _app.Ime.ActiveAppKey;
        _lblActiveApp.Text = key.Length == 0
            ? "Ứng dụng đang ở trước: (chưa xác định)"
            : $"Ứng dụng đang ở trước: {_app.Ime.ActiveAppName} ({key}) — " +
              (_app.AppModes.GetMode(key) ? "K — Hán Nôm" : "Q — Latin");
    }

    /// <summary>
    /// Ứng dụng ở trước vừa đổi. Cửa sổ tuỳ chọn đang mở thì vẽ lại danh sách để
    /// người dùng thấy ngay chế độ của ứng dụng vừa chuyển sang.
    /// </summary>
    public void OnActiveAppChanged() => LoadAppList();

    private string? SelectedAppKey => (_gridApps?.SelectedItem as AppModeRow)?.Process;

    private void ToggleSelectedApp()
    {
        if (SelectedAppKey is not { Length: > 0 } key) return;

        var store = _app.AppModes;
        store.SetMode(key, "", !store.GetMode(key));
        store.Save();

        // Nếu đó chính là ứng dụng đang ở trước thì bộ gõ phải đổi theo ngay,
        // chứ không đợi người dùng bấm ra bấm vào cửa sổ đó một lượt.
        _app.SyncActiveAppMode();

        LoadAppList();
        SetStatus($"Đã đổi chế độ cho \u201C{key}\u201D.");
    }

    private void ForgetSelectedApp()
    {
        if (SelectedAppKey is not { Length: > 0 } key) return;

        var store = _app.AppModes;
        store.Remove(key);
        store.Save();
        LoadAppList();
        SetStatus($"Đã xoá \u201C{key}\u201D khỏi danh sách — lần sau ứng dụng đó mặc định gõ Latin.");
    }

    /// <summary>Mở config.json bằng chương trình mặc định để người dùng xem/sửa tay.</summary>
    private void OpenConfigFile()
    {
        var store = _app.AppModes;
        if (!File.Exists(store.FilePath)) store.Save();

        try
        {
            Process.Start(new ProcessStartInfo("open", store.FilePath) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus("Không mở được tệp: " + store.FilePath);
        }
    }

    private void UpdatePerAppEnabled()
    {
        bool on = _cbPerApp.IsChecked == true;
        _gridApps.IsEnabled = on;
        _lblActiveApp.IsEnabled = on;
    }

    private async Task ResetDefaults()
    {
        bool yes = await Dialogs.Confirm(this, "KeyGo Hán Việt",
            "Khôi phục toàn bộ tuỳ chọn về mặc định?", "Khôi phục", "Huỷ");
        if (!yes) return;

        var fresh = new AppSettings
        {
            MainDictionaryPath = _draft.MainDictionaryPath,
            UserDictionaryPath = _draft.UserDictionaryPath,
        };
        _draft = fresh;
        LoadFromSettings();
        SetStatus("Đã khôi phục mặc định — nhấn “Áp dụng” để lưu.");
    }

    // --------------------------------------------------------- gõ tắt

    private void LoadMacrosToGrid()
    {
        _macroRows.Clear();
        foreach (var m in _app.Macros.Items)
            _macroRows.Add(new MacroRow
            {
                Key = m.Key,
                Values = string.Join(" | ", m.Values),
                Note = m.Note ?? "",
            });
    }

    private void SaveMacros()
    {
        var items = new List<MacroItem>();

        foreach (var row in _macroRows)
        {
            string key = row.Key.Trim();
            string vals = row.Values.Trim();
            string note = row.Note.Trim();

            if (key.Length == 0 || vals.Length == 0) continue;

            items.Add(new MacroItem
            {
                Key = key,
                Values = vals.Split('|', StringSplitOptions.RemoveEmptyEntries)
                             .Select(v => v.Trim())
                             .Where(v => v.Length > 0)
                             .ToList(),
                Note = note.Length > 0 ? note : null,
            });
        }

        _app.Macros.Replace(items);
        _app.Macros.Save();
        _app.ReloadDictionary();

        SetStatus($"Đã lưu {items.Count} mục gõ tắt.");
        RunQuery();
    }

    // -------------------------------------------------------- tra thử

    private void RunQuery()
    {
        if (_txtQuery is null) return;

        string q = (_txtQuery.Text ?? "").Trim();
        _results.Clear();

        if (q.Length == 0)
        {
            _lblPreedit.Text = "";
            return;
        }

        var list = _app.Engine.GetCandidates(q);
        _lblPreedit.Text = "Quốc ngữ: " + _app.Engine.RenderPreedit(q, list.FirstOrDefault());

        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i];
            _results.Add(new LookupRow
            {
                Index = (i + 1).ToString(),
                Text = c.Text,
                Tier = TierName(c.Tier),
                Weight = c.Weight == int.MaxValue ? "—" : c.Weight.ToString(),
                Usage = c.UsageCount.ToString(),
                Hint = c.Hint,
            });
        }

        if (list.Count == 0)
            _results.Add(new LookupRow { Text = "(không có gợi ý)" });
    }

    private static string TierName(CandidateTier tier) => tier switch
    {
        CandidateTier.Macro => "Gõ tắt",
        CandidateTier.ExactFull => "Đủ dấu",
        CandidateTier.ExactBare => "Bỏ thanh",
        CandidateTier.ExactPlain => "Không dấu",
        CandidateTier.Composite => "Ghép cụm",
        CandidateTier.Abbrev => "Viết tắt",
        CandidateTier.Prefix => "Gõ dở",
        _ => tier.ToString(),
    };

    private void UpdateDictStats()
    {
        var d = _app.Dict;
        _lblDictStats.Text =
            $"Đã nạp {d.EntryCount:N0} mục ({d.PhraseCount:N0} cụm nhiều chữ, {d.UserEntryCount:N0} mục người dùng) " +
            $"trong {d.LoadMilliseconds} ms · {_app.Macros.Count} mục gõ tắt.\n" +
            $"{d.VietEntryCount:N0} mục có sẵn cột âm đọc Quốc ngữ trong từ điển; " +
            "số còn lại vẫn tra được bằng âm đọc, nhưng âm đọc là dựng lại từ mã TELEX.";
    }

    // --------------------------------------------------------- tiện ích

    public void OnDictionaryReloaded()
    {
        UpdateDictStats();
        LoadMacrosToGrid();
        RunQuery();
    }

    public void OnModeChanged()
    {
        if (_loading) return;
        _loading = true;
        _rbHan.IsChecked = _app.Settings.HanNomMode;
        _rbLatin.IsChecked = !_app.Settings.HanNomMode;
        _cbEnabled.IsChecked = _app.Settings.Enabled;
        _loading = false;
    }

    private void OnCommitted(object? sender, string text) => SetStatus($"Đã xuất: {text}");

    private void SetStatus(string text) => _status.Text = text;

    /// <summary>Ô nhập tổ hợp chỉ dùng được khi đã chọn kiểu "Tự đặt tổ hợp phím".</summary>
    private void UpdateHotkeyEnabled()
    {
        if (_txtHotkey is null || _lblHotkey is null) return;

        int i = _cbToggleKey.SelectedIndex;
        bool custom = i >= 0 && i < ToggleOrder.Length && ToggleOrder[i] == ToggleKeyKind.Custom;

        _txtHotkey.IsEnabled = custom;
        _lblHotkey.IsEnabled = custom;
    }

    /// <summary>Hai phím tắt clipboard chỉ có nghĩa khi đã bật tra theo âm đọc Quốc ngữ.</summary>
    private void UpdateVietEnabled()
    {
        if (_txtClipboardKey is null || _txtSelectionKey is null) return;

        bool on = _cbViet.IsChecked == true;
        _txtClipboardKey.IsEnabled = on;
        _txtSelectionKey.IsEnabled = on;
    }

    /// <summary>
    /// Phím tắt xem âm đọc không phụ thuộc tuỳ chọn tra theo Quốc ngữ ở trên:
    /// đây là chiều ngược lại, không dính gì tới việc gõ chữ.
    /// </summary>
    private void UpdateHanToVietEnabled()
    {
        if (_txtHanToVietKey is null) return;
        _txtHanToVietKey.IsEnabled = _cbHanToViet.IsChecked == true;
    }

    private async Task Browse(TextBox target)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Chọn tệp từ điển",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Từ điển Rime") { Patterns = ["*.yaml", "*.txt"] },
                new FilePickerFileType("Tất cả tệp") { Patterns = ["*"] },
            ],
        });

        if (files.Count > 0) target.Text = files[0].Path.LocalPath;
    }

    private void OpenInEditor(string path)
    {
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, "", Encoding.UTF8);

            // "open" là lệnh chuẩn của macOS để mở tệp bằng ứng dụng mặc định,
            // tương đương UseShellExecute của Windows.
            Process.Start(new ProcessStartInfo("open", path) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            _ = Dialogs.ShowMessage(this, "KeyGo Hán Việt", $"Không mở được tệp:\n{ex.Message}");
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Thu về thanh trình đơn thay vì đóng hẳn, giống bản Windows. Không áp
        // dụng khi chính chương trình đang thoát, nếu không sẽ không thoát được.
        if (!e.IsProgrammatic && _app.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _app.Ime.Committed -= OnCommitted;
        base.OnClosing(e);
    }
}
