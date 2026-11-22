using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using KeyGoHanViet.Core;
using KeyGoHanViet.Mac.Interop;

namespace KeyGoHanViet.Mac.UI;

/// <summary>
/// Cửa sổ nhỏ hiện âm đọc Quốc ngữ của đoạn chữ Hán Nôm đang bôi đen.
/// Bản macOS của <c>UI\HanVietPopupForm.cs</c> bên Windows.
///
/// Khác hẳn <see cref="CandidateWindow"/> ở một điểm cốt lõi: cửa sổ này <b>có</b>
/// nhận tiêu điểm. Khung gợi ý phải né tiêu điểm để chữ gõ ra đúng chỗ, còn ở
/// đây người dùng cần bôi đen và sao chép kết quả - không có tiêu điểm thì không
/// làm được việc đó.
///
/// Đổi lại, cửa sổ tự đóng ngay khi mất tiêu điểm, nên nó không bao giờ nằm chắn
/// trước các ứng dụng khác: bấm ra ngoài, bấm nút đóng, hay nhấn Esc đều đóng.
/// </summary>
public sealed class HanVietPopupWindow : Window
{
    private readonly AppSettings _settings;
    private readonly HanVietResult _result;

    private readonly TextBlock _source;
    private readonly TextBox _text;
    private readonly TextBlock _status;

    /// <summary>
    /// Tạm khoá việc tự đóng khi mất tiêu điểm - trình đơn chọn âm đọc là một
    /// cửa sổ riêng, mở nó ra có thể bị coi là cửa sổ này mất tiêu điểm.
    /// </summary>
    private bool _keepOpen;

    private bool _closing;

    public HanVietPopupWindow(AppSettings settings, HanVietResult result)
    {
        _settings = settings;
        _result = result;

        Title = "Hán → Việt";
        Icon = MacTrayIcons.App;
        Width = 480;
        Height = 280;
        MinWidth = 320;
        MinHeight = 200;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = BackBrush;

        _source = new TextBlock
        {
            FontFamily = HanFont,
            FontSize = Math.Max(12, _settings.HanFontSize - 2),
            Foreground = HintBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _text = new TextBox
        {
            // Chỉ đọc chứ không cấm chọn: bôi đen và ⌘C vẫn chạy như thường, mà
            // không lo người dùng lỡ tay sửa rồi tưởng bộ gõ dịch sai.
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = _settings.UiFontSize + 8,
            Foreground = TextBrush,
            Height = 120,
        };
        _text.PointerReleased += OnTextPointerReleased;

        _status = new TextBlock
        {
            Foreground = HintBrush,
            TextWrapping = TextWrapping.Wrap,
            FontSize = _settings.UiFontSize + 2,
        };

        var copy = new Button { Content = "Sao chép", MinWidth = 100, IsDefault = true };
        copy.Click += (_, _) => CopyResult();

        var close = new Button { Content = "Đóng", MinWidth = 90 };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                _source,
                _text,
                _status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { copy, close },
                },
            },
        };

        Deactivated += (_, _) => CloseOnFocusLost();

        Refill();
    }

    // -------------------------------------------------------------- giao diện

    private bool Dark => _settings.DarkTheme;

    private IBrush BackBrush => Brush(Dark ? (32, 34, 38) : (252, 252, 253));
    private IBrush TextBrush => Brush(Dark ? (238, 238, 240) : (24, 26, 30));
    private IBrush HintBrush => Brush(Dark ? (140, 146, 158) : (112, 118, 130));

    private static IBrush Brush((int R, int G, int B) c) =>
        new SolidColorBrush(Color.FromRgb((byte)c.R, (byte)c.G, (byte)c.B));

    private FontFamily HanFont => new(
        $"{_settings.HanFontFamily}, Nom Na Tong, HanaMinB, Hiragino Sans, PingFang SC, Apple SD Gothic Neo");

    // -------------------------------------------------------------- nội dung

    /// <summary>Dựng lại nội dung sau khi người dùng đổi âm đọc của một chữ.</summary>
    private void Refill()
    {
        _source.Text = _result.Source.Replace("\r", " ").Replace("\n", " ");
        _text.Text = _result.Render();

        int alternatives = _result.Tokens.Count(t => t.HasAlternatives);

        string note = _result.HanCount == 0
            ? "Đoạn đang chọn không có chữ Hán Nôm nào."
            : $"{_result.HanCount} chữ Hán" +
              (_result.UnknownCount > 0 ? $" · {_result.UnknownCount} chữ không có trong từ điển" : "") +
              (alternatives > 0 ? $" · {alternatives} chữ nhiều âm: bấm vào chữ để đổi" : "") +
              (_result.Truncated ? " · đã cắt bớt vì đoạn quá dài" : "");

        _status.Text = note + "\n⌘C sao chép · Esc hoặc bấm ra ngoài để đóng";
    }

    private void CopyResult()
    {
        string text = _text.Text ?? "";
        if (text.Length == 0) return;

        // Dùng thẳng NSPasteboard chứ không dùng clipboard của Avalonia: hàm ở
        // đó chạy bất đồng bộ, mà ở đây không có gì phải chờ.
        _status.Text = Pasteboard.SetString(text)
            ? "Đã sao chép vào clipboard.\nEsc hoặc bấm ra ngoài để đóng"
            : "Không ghi được clipboard.\nThử lại sau giây lát.";
    }

    // ----------------------------------------------------- đổi âm đọc

    /// <summary>
    /// Bấm vào một chữ trong kết quả thì hiện danh sách các âm đọc khác của chữ
    /// Hán tương ứng. Chữ Hán Nôm đa âm là chuyện thường, mà máy thì chỉ đoán
    /// được âm hay gặp nhất - phần còn lại phải để người đọc tự quyết.
    /// </summary>
    private void OnTextPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Con trỏ nhập liệu đã nhảy tới chỗ vừa bấm, nên lấy luôn vị trí của nó
        // thay vì tự tính lại từ toạ độ chuột.
        var token = _result.TokenAt(_text.CaretIndex);
        if (token is null || !token.HasAlternatives) return;

        var menu = new MenuFlyout { Placement = PlacementMode.Pointer };
        var items = new List<MenuItem>(token.Readings.Count);

        for (int i = 0; i < token.Readings.Count; i++)
        {
            int index = i;
            var item = new MenuItem
            {
                Header = i == token.Chosen ? $"● {token.Readings[i]}" : $"    {token.Readings[i]}",
            };
            item.Click += (_, _) =>
            {
                token.Chosen = index;
                Refill();
            };
            items.Add(item);
        }

        menu.ItemsSource = items;

        // Trình đơn mở ra là một cửa sổ khác: giữ cửa sổ này sống cho tới khi
        // trình đơn đóng lại, nếu không cả hai cùng biến mất.
        _keepOpen = true;
        menu.Closed += (_, _) =>
        {
            _keepOpen = false;
            if (!IsActive) CloseOnFocusLost();
        };

        menu.ShowAt(_text, showAtPointer: true);
    }

    // -------------------------------------------------------------- vòng đời

    /// <summary>Hiện cửa sổ ngay cạnh chỗ người dùng đang đọc, không tràn ra ngoài màn hình.</summary>
    public void ShowNearCaret()
    {
        var caret = MacCaretLocator.GetCaretRect();

        // Trợ năng trả toạ độ theo ĐIỂM, còn Window.Position tính theo ĐIỂM ẢNH -
        // cùng phép quy đổi như khung gợi ý, xem ghi chú trong CandidateWindow.
        var screen = Screens.ScreenFromPoint(new PixelPoint((int)caret.X, (int)caret.Y)) ?? Screens.Primary;
        double scale = screen?.Scaling ?? 1.0;

        int caretTop = (int)Math.Round(caret.Y * scale);
        int x = (int)Math.Round(caret.X * scale);
        int y = (int)Math.Round(caret.Bottom * scale) + (int)Math.Round(6 * scale);

        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1440, 900);
        int width = (int)Math.Round(Width * scale);
        int height = (int)Math.Round(Height * scale);

        if (x + width > area.Right) x = area.Right - width - 8;
        if (x < area.X) x = area.X + 8;
        if (y + height > area.Bottom) y = Math.Max(area.Y + 8, caretTop - height - 6);
        if (y < area.Y) y = area.Y + 8;

        Position = new PixelPoint(x, y);

        Show();
        Activate();
        _text.Focus();
        _text.SelectAll();
    }

    private void CloseOnFocusLost()
    {
        if (_keepOpen || _closing) return;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }
}
