using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using KeyGoHanViet.Core;
using KeyGoHanViet.Engine;
using KeyGoHanViet.Mac.Interop;

namespace KeyGoHanViet.Mac.UI;

/// <summary>
/// Khung gợi ý nổi, bám sát caret và không cướp tiêu điểm của ứng dụng đích.
/// Bản macOS của <c>UI\CandidateWindow.cs</c> bên Windows.
///
/// Dựng hoàn toàn bằng mã chứ không qua tệp .axaml, cùng lý do với bản Windows:
/// nội dung khung đổi theo từng phím gõ nên vẫn phải dựng lại bằng mã, thêm một
/// tệp giao diện nữa chỉ làm hai chỗ dễ lệch nhau.
/// </summary>
public sealed class CandidateWindow : Window
{
    private readonly AppSettings _settings;
    private readonly StackPanel _root;
    private readonly Border _preeditBox;
    private readonly TextBlock _preeditText;
    private readonly StackPanel _rows;

    /// <summary>Dòng chân khung "trang 2/6 · 46 chữ · Tab ▸", ẩn khi chỉ có một trang.</summary>
    private readonly Border _pageBox;
    private readonly TextBlock _pageText;

    private IReadOnlyList<Candidate> _candidates = [];
    private int _selected;
    private string _preedit = "";
    private string _raw = "";

    public event EventHandler<int>? CandidateClicked;

    public CandidateWindow(AppSettings settings)
    {
        _settings = settings;

        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Không được nhận tiêu điểm, nếu không caret của ứng dụng đích sẽ mất
        // và chữ sẽ xuất ra nhầm chỗ.
        ShowActivated = false;
        Focusable = false;

        _preeditText = new TextBlock
        {
            Margin = new Thickness(10, 5, 10, 5),
            FontWeight = FontWeight.SemiBold,
        };

        _preeditBox = new Border
        {
            Child = _preeditText,
            IsVisible = false,
        };

        _rows = new StackPanel { Orientation = Orientation.Vertical };

        _pageText = new TextBlock { Margin = new Thickness(10, 3, 10, 3) };

        _pageBox = new Border
        {
            Child = _pageText,
            BorderThickness = new Thickness(0, 1, 0, 0),
            IsVisible = false,
        };

        _root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children = { _preeditBox, _rows, _pageBox },
        };

        Content = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 4, 0, 4),
            Child = _root,
        };

        ApplyTheme();
    }

    // -------------------------------------------------------------- giao diện

    private bool Dark => _settings.DarkTheme;

    private IBrush BackBrush => Brush(Dark ? (32, 34, 38) : (252, 252, 253));
    private IBrush BorderBrush2 => Brush(Dark ? (70, 74, 82) : (196, 200, 208));
    private IBrush TextBrush => Brush(Dark ? (238, 238, 240) : (24, 26, 30));
    private IBrush HintBrush => Brush(Dark ? (140, 146, 158) : (122, 128, 140));
    private IBrush SelectBrush => Brush(Dark ? (52, 88, 140) : (214, 231, 255));
    private IBrush PreeditBackBrush => Brush(Dark ? (42, 45, 51) : (240, 242, 246));

    private static IBrush Brush((int R, int G, int B) c) =>
        new SolidColorBrush(Color.FromRgb((byte)c.R, (byte)c.G, (byte)c.B));

    /// <summary>Áp lại màu và phông sau khi người dùng đổi tuỳ chọn giao diện.</summary>
    public void ApplyTheme()
    {
        Background = BackBrush;

        if (Content is Border frame)
        {
            frame.Background = BackBrush;
            frame.BorderBrush = BorderBrush2;
        }

        _preeditBox.Background = PreeditBackBrush;
        _preeditText.Foreground = TextBrush;
        _preeditText.FontFamily = UiFont;
        _preeditText.FontSize = _settings.UiFontSize + 4;

        _pageBox.BorderBrush = BorderBrush2;
        _pageText.Foreground = HintBrush;
        _pageText.FontFamily = UiFont;
        _pageText.FontSize = _settings.UiFontSize + 2;

        _rows.Orientation = _settings.Layout == CandidateLayout.Horizontal
            ? Orientation.Horizontal
            : Orientation.Vertical;

        Rebuild();
    }

    /// <summary>
    /// Phông chữ Hán Nôm, kèm danh sách lùi. Avalonia nhận cả chuỗi nhiều phông
    /// ngăn cách bằng dấu phẩy và tự dùng phông đầu tiên có trên máy, nên chỉ
    /// cần nối tên người dùng chọn vào trước các phông dự phòng.
    /// </summary>
    private FontFamily HanFont => new(
        $"{_settings.HanFontFamily}, Nom Na Tong, HanaMinB, Hiragino Sans, PingFang SC, Apple SD Gothic Neo");

    private FontFamily UiFont => new($"{_settings.UiFontFamily}, -apple-system, Helvetica Neue");

    // ------------------------------------------------------------- cập nhật

    public void UpdateState(ImeStateEventArgs state)
    {
        _candidates = state.Candidates;
        _selected = state.SelectedIndex;
        _preedit = state.Preedit;
        _raw = state.RawInput;

        // Chỉ một trang thì để trống: nói "trang 1/1" cho danh sách ngắn chỉ tổ
        // làm khung cao thêm mà không thêm tin gì.
        _pageBox.IsVisible = state.PageCount > 1;
        _pageText.Text = state.PageCount > 1
            ? $"trang {state.PageIndex}/{state.PageCount} · {state.TotalCandidates} chữ · Tab ▸"
            : "";

        if (!state.Composing || (_candidates.Count == 0 && _raw.Length == 0))
        {
            HidePopup();
            return;
        }

        Rebuild();
        ShowAtCaret();
    }

    public void HidePopup()
    {
        if (IsVisible) Hide();
    }

    private void Rebuild()
    {
        _preeditBox.IsVisible = _settings.ShowPreedit && _preedit.Length > 0;
        _preeditText.Text = _preedit;

        _rows.Children.Clear();

        for (int i = 0; i < _candidates.Count; i++)
            _rows.Children.Add(BuildRow(i, _candidates[i]));
    }

    /// <summary>Chỉ tô lại dòng đang chọn, không dựng lại danh sách.</summary>
    private void RefreshSelection()
    {
        for (int i = 0; i < _rows.Children.Count; i++)
            if (_rows.Children[i] is Border row)
                row.Background = i == _selected ? SelectBrush : Brushes.Transparent;
    }

    private Control BuildRow(int index, Candidate candidate)
    {
        var number = new TextBlock
        {
            Text = (index + 1).ToString(),
            Foreground = HintBrush,
            FontFamily = UiFont,
            FontSize = _settings.UiFontSize + 3,
            Width = 20,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var text = new TextBlock
        {
            Text = candidate.Text,
            Foreground = TextBrush,
            FontFamily = HanFont,
            FontSize = _settings.HanFontSize,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { number, text },
        };

        if (_settings.ShowHints && candidate.Hint.Length > 0)
        {
            line.Children.Add(new TextBlock
            {
                Text = candidate.Hint,
                Foreground = HintBrush,
                FontFamily = UiFont,
                FontSize = _settings.UiFontSize + 2,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
            });
        }

        var row = new Border
        {
            Child = line,
            Padding = new Thickness(10, 2, 10, 2),
            Background = index == _selected ? SelectBrush : Brushes.Transparent,
        };

        // Bấm chuột vào một dòng tương đương gõ phím số của dòng đó.
        row.PointerPressed += (_, _) => CandidateClicked?.Invoke(this, index);

        // Rê chuột chỉ đổi màu nền chứ không dựng lại cả danh sách: dựng lại sẽ
        // huỷ đúng cái Border vừa phát ra sự kiện này.
        row.PointerEntered += (_, _) =>
        {
            if (_selected == index) return;
            _selected = index;
            RefreshSelection();
        };

        return row;
    }

    // ------------------------------------------------------------- vị trí

    private void ShowAtCaret()
    {
        var caret = MacCaretLocator.GetCaretRect();

        // Trợ năng trả toạ độ theo ĐIỂM, còn Window.Position của Avalonia tính
        // theo ĐIỂM ẢNH. Trên màn Retina hai đơn vị này chênh nhau đúng hệ số
        // Scaling, không nhân vào thì khung gợi ý rơi lệch hẳn sang góc trên trái.
        var screen = Screens.ScreenFromPoint(new PixelPoint((int)caret.X, (int)caret.Y))
                     ?? Screens.Primary;
        double scale = screen?.Scaling ?? 1.0;

        int caretLeft = (int)Math.Round(caret.X * scale);
        int caretTop = (int)Math.Round(caret.Y * scale);
        int caretBottom = (int)Math.Round(caret.Bottom * scale);

        // Sau khi màn hình được xác định lại theo điểm ảnh, tính lại hệ số một
        // lần nữa: hai màn hình cạnh nhau có thể khác độ phân giải.
        var target = Screens.ScreenFromPoint(new PixelPoint(caretLeft, caretTop)) ?? screen;
        var area = target?.WorkingArea ?? new PixelRect(0, 0, 1440, 900);

        var size = MeasuredSize();
        int width = (int)Math.Round(size.Width * scale);
        int height = (int)Math.Round(size.Height * scale);

        int x = caretLeft;
        int y = caretBottom + (int)Math.Round(4 * scale);

        if (x + width > area.Right) x = area.Right - width - 4;
        if (x < area.X) x = area.X + 4;

        // Không đủ chỗ phía dưới thì lật khung lên trên caret.
        if (y + height > area.Bottom) y = caretTop - height - (int)Math.Round(4 * scale);
        if (y < area.Y) y = area.Y + 4;

        Position = new PixelPoint(x, y);

        if (!IsVisible) Show();
    }

    /// <summary>
    /// Cỡ khung tính theo điểm.
    ///
    /// Không đọc thẳng <c>Bounds</c> được: lần đầu hiện khung, cửa sổ chưa qua
    /// một vòng bố trí nào nên Bounds vẫn là 0×0, và phép căn lề bên dưới sẽ
    /// tưởng khung không chiếm chỗ nào cả. Tự đo nội dung thì có số đúng ngay
    /// từ lần đầu, lại tránh được cảnh khung nhảy một nhịp sau khi hiện.
    /// </summary>
    private Size MeasuredSize()
    {
        if (Content is not Control content) return new Size(160, 40);

        content.Measure(Size.Infinity);
        var desired = content.DesiredSize;

        // Bounds đã có thì tin nó hơn, vì đó là cỡ thật sau khi bố trí xong.
        double w = Bounds.Width > 0 ? Bounds.Width : desired.Width;
        double h = Bounds.Height > 0 ? Bounds.Height : desired.Height;

        return new Size(Math.Max(w, 160), Math.Max(h, 24));
    }
}
