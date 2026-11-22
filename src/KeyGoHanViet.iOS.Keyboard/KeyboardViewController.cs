using CoreGraphics;
using Foundation;
using KeyGoHanViet.Core;
using System.Runtime.InteropServices;
using ObjCRuntime;
using UIKit;

namespace KeyGoHanViet.iOS.Keyboard;

/// <summary>
/// Bàn phím KeyGo Hán Việt cho iPhone / iPad.
///
/// Chế độ 漢: phím chữ gom vào bộ đệm TELEX, thanh trên cùng hiện âm Quốc ngữ
/// và các chữ Hán gợi ý. Chạm một chữ để chèn; dấu cách chèn chữ đầu tiên;
/// phím xuống dòng chèn âm Quốc ngữ; số 1-9 chọn theo thứ tự.
/// Chế độ En: phím nào chèn thẳng chữ đó, như bàn phím thường.
/// </summary>
[Register("KeyboardViewController")]
public sealed class KeyboardViewController : UIInputViewController
{
    // ---------------------------------------------------------------- bố cục

    private const double BarHeight = 52;
    private const double RowPitch = 54;
    private const double KeyHeight = 42;
    private const double Side = 3;
    private const double Gap = 6;
    private const double KeyboardHeight = BarHeight + RowPitch * 4 + 4;
    private const int MaxShown = 60;

    private enum KeyKind { Char, Shift, Backspace, Globe, Layer, Mode, Space, Return }

    private sealed class Key
    {
        public required UIButton Button { get; init; }
        public required KeyKind Kind { get; init; }
        public string Value { get; init; } = "";
        public double Units { get; init; } = 1;
    }

    private static readonly string[][] LetterRows =
    [
        ["q", "w", "e", "r", "t", "y", "u", "i", "o", "p"],
        ["a", "s", "d", "f", "g", "h", "j", "k", "l"],
        ["z", "x", "c", "v", "b", "n", "m"],
    ];

    private static readonly string[][] SymbolRows =
    [
        ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"],
        ["-", "/", ":", ";", "(", ")", "「", "」", "\"", "@"],
        ["。", "，", "？", "！", ".", ",", "?"],
    ];

    private readonly List<List<Key>> _letters = [];
    private readonly List<List<Key>> _symbols = [];

    private UIView _bar = null!;
    private UILabel _preedit = null!;
    private UIScrollView _scroll = null!;
    private readonly List<UIButton> _candidateButtons = [];

    // ---------------------------------------------------------------- trạng thái

    private const string LatinModeKey = "KeyGoLatinMode";

    private CandidateEngine? _engine;
    private string _buffer = "";
    private List<Candidate> _candidates = [];
    private bool _hanMode = true;
    private bool _symbolLayer;
    private bool _shift;
    private NSTimer? _repeatTimer;

    public KeyboardViewController() { }

    public KeyboardViewController(NativeHandle handle) : base(handle) { }

    // ---------------------------------------------------------------- vòng đời

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();

        _hanMode = !NSUserDefaults.StandardUserDefaults.BoolForKey(LatinModeKey);

        var height = View!.HeightAnchor.ConstraintEqualTo(new NFloat(KeyboardHeight));
        height.Priority = 999f;
        height.Active = true;

        BuildBar();
        BuildKeys();
        UpdateKeyLabels();
        Refresh();

        _ = LoadEngineAsync();
    }

    private async Task LoadEngineAsync()
    {
        var engine = await EngineHost.GetAsync();
        InvokeOnMainThread(() =>
        {
            _engine = engine;
            Refresh();
        });
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        LayoutAll();
    }

    public override void ViewWillDisappear(bool animated)
    {
        base.ViewWillDisappear(animated);
        StopRepeat();
        _buffer = "";
        Refresh();
        _engine?.Usage.Save();
    }

    public override void TextDidChange(IUITextInput? textInput)
    {
        base.TextDidChange(textInput);
        UpdateKeyLabels();
    }

    // ---------------------------------------------------------------- dựng giao diện

    private void BuildBar()
    {
        _bar = new UIView();
        View!.AddSubview(_bar);

        _preedit = new UILabel
        {
            Font = UIFont.SystemFontOfSize(13),
            TextColor = UIColor.SecondaryLabel,
            LineBreakMode = UILineBreakMode.HeadTruncation,
        };
        _bar.AddSubview(_preedit);

        _scroll = new UIScrollView
        {
            ShowsHorizontalScrollIndicator = false,
            ShowsVerticalScrollIndicator = false,
            AlwaysBounceHorizontal = true,
        };
        _bar.AddSubview(_scroll);
    }

    private void BuildKeys()
    {
        BuildLayer(_letters, LetterRows, leftWide: null);
        BuildLayer(_symbols, SymbolRows, leftWide: "、");
        ShowLayer();
    }

    /// <summary>
    /// Dựng ba hàng ký tự + hàng chức năng. Hàng thứ ba có phím rộng hai bên:
    /// Shift (hoặc ký tự <paramref name="leftWide"/> ở bảng ký hiệu) và Xoá.
    /// </summary>
    private void BuildLayer(List<List<Key>> layer, string[][] rows, string? leftWide)
    {
        for (int r = 0; r < rows.Length; r++)
        {
            var row = new List<Key>();
            if (r == 2)
                row.Add(leftWide is null
                    ? MakeKey(KeyKind.Shift, "", special: true)
                    : MakeKey(KeyKind.Char, leftWide, special: true));

            foreach (string c in rows[r])
                row.Add(MakeKey(KeyKind.Char, c, special: false));

            if (r == 2)
                row.Add(MakeKey(KeyKind.Backspace, "", special: true));
            layer.Add(row);
        }

        layer.Add(
        [
            MakeKey(KeyKind.Globe, "", special: true, units: 1.3),
            MakeKey(KeyKind.Layer, "", special: true, units: 1.3),
            MakeKey(KeyKind.Mode, "", special: true, units: 1.3),
            MakeKey(KeyKind.Space, "", special: false),
            MakeKey(KeyKind.Return, "", special: true, units: 2.2),
        ]);
    }

    private Key MakeKey(KeyKind kind, string value, bool special, double units = 1)
    {
        var button = new UIButton(UIButtonType.Custom);
        button.BackgroundColor = special ? Palette.SpecialKey : Palette.Key;
        button.Layer.CornerRadius = 6;
        button.Layer.ShadowColor = UIColor.Black.CGColor;
        button.Layer.ShadowOffset = new CGSize(0, 1);
        button.Layer.ShadowOpacity = 0.3f;
        button.Layer.ShadowRadius = 0;
        button.TintColor = UIColor.Label;
        button.SetTitleColor(UIColor.Label, UIControlState.Normal);
        button.TitleLabel!.Font = kind == KeyKind.Char
            ? UIFont.SystemFontOfSize(23)
            : UIFont.SystemFontOfSize(16);
        View!.AddSubview(button);

        var key = new Key { Button = button, Kind = kind, Value = value, Units = units };

        switch (kind)
        {
            case KeyKind.Globe:
                button.SetImage(Symbol("globe"), UIControlState.Normal);
                // Bấm = sang bàn phím kế tiếp, giữ = hiện danh sách bàn phím.
                button.AddTarget(this, new Selector("handleInputModeListFromView:withEvent:"),
                                 UIControlEvent.AllTouchEvents);
                break;

            case KeyKind.Backspace:
                button.SetImage(Symbol("delete.left"), UIControlState.Normal);
                button.TouchDown += (_, _) => StartRepeat();
                button.TouchUpInside += (_, _) => StopRepeat();
                button.TouchUpOutside += (_, _) => StopRepeat();
                button.TouchCancel += (_, _) => StopRepeat();
                break;

            case KeyKind.Return:
                button.SetImage(Symbol("return"), UIControlState.Normal);
                button.TouchUpInside += (_, _) => OnReturn();
                break;

            default:
                button.TouchUpInside += (_, _) => OnKey(key);
                break;
        }

        // Chạm vào thì sáng lên một chút, như phím của iOS.
        button.TouchDown += (_, _) => button.Alpha = 0.6f;
        button.TouchUpInside += (_, _) => button.Alpha = 1f;
        button.TouchUpOutside += (_, _) => button.Alpha = 1f;
        button.TouchCancel += (_, _) => button.Alpha = 1f;
        return key;
    }

    private static CGRect Rect(double x, double y, double w, double h) =>
        new(new NFloat(x), new NFloat(y), new NFloat(w), new NFloat(h));

    private static CGSize Size(double w, double h) => new(new NFloat(w), new NFloat(h));

    private static UIImage? Symbol(string name) =>
        UIImage.GetSystemImage(name, UIImageSymbolConfiguration.Create(UIFont.SystemFontOfSize(18)));

    private void ShowLayer()
    {
        foreach (var row in _letters) foreach (var k in row) k.Button.Hidden = _symbolLayer;
        foreach (var row in _symbols) foreach (var k in row) k.Button.Hidden = !_symbolLayer;

        if (!NeedsInputModeSwitchKey)
            foreach (var layer in new[] { _letters, _symbols })
                foreach (var row in layer)
                    foreach (var k in row)
                        if (k.Kind == KeyKind.Globe) k.Button.Hidden = true;

        View?.SetNeedsLayout();
    }

    private void UpdateKeyLabels()
    {
        bool upper = _shift && !_hanMode;
        foreach (var layer in new[] { _letters, _symbols })
            foreach (var row in layer)
                foreach (var k in row)
                {
                    switch (k.Kind)
                    {
                        case KeyKind.Char:
                            string text = upper && layer == _letters ? k.Value.ToUpperInvariant() : k.Value;
                            k.Button.SetTitle(text, UIControlState.Normal);
                            break;
                        case KeyKind.Shift:
                            k.Button.SetImage(Symbol(upper ? "shift.fill" : "shift"), UIControlState.Normal);
                            k.Button.Enabled = !_hanMode;
                            k.Button.Alpha = _hanMode ? 0.4f : 1f;
                            break;
                        case KeyKind.Layer:
                            k.Button.SetTitle(_symbolLayer ? "ABC" : "123", UIControlState.Normal);
                            break;
                        case KeyKind.Mode:
                            k.Button.SetTitle(_hanMode ? "漢" : "En", UIControlState.Normal);
                            k.Button.TitleLabel!.Font = UIFont.SystemFontOfSize(_hanMode ? 20 : 16);
                            break;
                        case KeyKind.Space:
                            k.Button.SetTitle(_hanMode ? "KeyGo 漢" : "dấu cách", UIControlState.Normal);
                            k.Button.SetTitleColor(UIColor.SecondaryLabel, UIControlState.Normal);
                            k.Button.TitleLabel!.Font = UIFont.SystemFontOfSize(15);
                            break;
                    }
                }
    }

    // ---------------------------------------------------------------- xếp vị trí

    private void LayoutAll()
    {
        double w = View!.Bounds.Width;
        if (w <= 0) return;

        _bar.Frame = Rect(0, 0, w, BarHeight);
        _preedit.Frame = Rect(10, 2, w - 20, 16);
        _scroll.Frame = Rect(0, 18, w, BarHeight - 18);
        LayoutCandidates();

        double avail = w - 2 * Side;
        double unit = (avail - 9 * Gap) / 10;

        foreach (var layer in new[] { _letters, _symbols })
            for (int r = 0; r < layer.Count; r++)
            {
                var row = layer[r].Where(k => k.Kind != KeyKind.Globe || NeedsInputModeSwitchKey).ToList();
                double y = BarHeight + r * RowPitch + (RowPitch - KeyHeight) / 2;
                var widths = new double[row.Count];

                if (r == 2)
                {
                    // Hai phím rộng hai bên chia đều phần còn lại, các phím giữa thẳng cột hàng trên.
                    int inner = row.Count - 2;
                    double wide = (avail - inner * unit - (inner + 1) * Gap) / 2;
                    for (int i = 0; i < row.Count; i++)
                        widths[i] = i == 0 || i == row.Count - 1 ? wide : unit;
                }
                else if (r == 3)
                {
                    double fixedSum = 0;
                    for (int i = 0; i < row.Count; i++)
                        if (row[i].Kind != KeyKind.Space)
                            fixedSum += widths[i] = row[i].Units * unit + (row[i].Units - 1) * Gap;
                    for (int i = 0; i < row.Count; i++)
                        if (row[i].Kind == KeyKind.Space)
                            widths[i] = avail - fixedSum - (row.Count - 1) * Gap;
                }
                else
                {
                    for (int i = 0; i < row.Count; i++) widths[i] = unit;
                }

                double total = widths.Sum() + (row.Count - 1) * Gap;
                double x = Side + (avail - total) / 2;
                for (int i = 0; i < row.Count; i++)
                {
                    row[i].Button.Frame = Rect(x, y, widths[i], KeyHeight);
                    x += widths[i] + Gap;
                }
            }
    }

    private void LayoutCandidates()
    {
        double x = 6;
        double h = _scroll.Bounds.Height;
        foreach (var b in _candidateButtons)
        {
            var size = b.SizeThatFits(Size(1000, h));
            double width = Math.Max((double)size.Width + 18, 40);
            b.Frame = Rect(x, 0, width, h);
            x += width + 2;
        }
        _scroll.ContentSize = Size(x + 6, h);
    }

    // ---------------------------------------------------------------- xử lý phím

    private void OnKey(Key key)
    {
        switch (key.Kind)
        {
            case KeyKind.Char:
                OnChar(_shift && !_hanMode && !_symbolLayer ? key.Value.ToUpperInvariant() : key.Value);
                if (_shift) { _shift = false; UpdateKeyLabels(); }
                break;

            case KeyKind.Shift:
                _shift = !_shift;
                UpdateKeyLabels();
                break;

            case KeyKind.Layer:
                _symbolLayer = !_symbolLayer;
                ShowLayer();
                UpdateKeyLabels();
                break;

            case KeyKind.Mode:
                CommitReading();
                _hanMode = !_hanMode;
                _shift = false;
                NSUserDefaults.StandardUserDefaults.SetBool(!_hanMode, LatinModeKey);
                UpdateKeyLabels();
                Refresh();
                break;

            case KeyKind.Space:
                if (_buffer.Length > 0)
                {
                    if (_candidates.Count > 0) Commit(_candidates[0]);
                    else CommitReading();
                }
                else Insert(" ");
                break;
        }
    }

    private void OnChar(string c)
    {
        bool letter = c.Length == 1 && c[0] is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

        if (_hanMode && letter)
        {
            _buffer += c.ToLowerInvariant();
            Refresh();
            return;
        }

        if (_hanMode && _buffer.Length > 0)
        {
            // Số 1-9 khi đang gõ dở: chọn ứng viên theo thứ tự.
            if (c.Length == 1 && c[0] is >= '1' and <= '9')
            {
                int index = c[0] - '1';
                if (index < _candidates.Count) { Commit(_candidates[index]); return; }
            }

            // Dấu câu: chốt chữ đang gõ rồi mới chèn dấu.
            if (_candidates.Count > 0) Commit(_candidates[0]);
            else CommitReading();
        }

        Insert(c);
    }

    private void OnReturn()
    {
        if (_buffer.Length > 0) CommitReading();
        else Insert("\n");
    }

    private void Backspace()
    {
        if (_buffer.Length > 0)
        {
            _buffer = _buffer[..^1];
            Refresh();
        }
        else
        {
            TextDocumentProxy.DeleteBackward();
        }
    }

    private void StartRepeat()
    {
        StopRepeat();
        Backspace();
        _repeatTimer = NSTimer.CreateScheduledTimer(0.45, _ =>
        {
            _repeatTimer = NSTimer.CreateRepeatingScheduledTimer(0.08, _ => Backspace());
        });
    }

    private void StopRepeat()
    {
        _repeatTimer?.Invalidate();
        _repeatTimer = null;
    }

    // ---------------------------------------------------------------- chèn chữ

    private void Insert(string text) => TextDocumentProxy.InsertText(text);

    private void Commit(Candidate c)
    {
        Insert(c.Text);
        _engine?.RecordChoice(_buffer, c.Text);
        _buffer = "";
        Refresh();
    }

    /// <summary>Chèn âm Quốc ngữ của chuỗi đang gõ (gõ tiếng Việt có dấu).</summary>
    private void CommitReading()
    {
        if (_buffer.Length == 0) return;
        string text = _engine?.RenderPreedit(_buffer) ?? _buffer;
        Insert(text);
        _buffer = "";
        Refresh();
    }

    // ---------------------------------------------------------------- thanh gợi ý

    private void Refresh()
    {
        if (_preedit is null) return;

        foreach (var b in _candidateButtons) b.RemoveFromSuperview();
        _candidateButtons.Clear();
        _candidates = [];

        if (_engine is null)
        {
            _preedit.Text = EngineHost.Error is { } err ? "Lỗi nạp từ điển: " + err : "Đang nạp từ điển…";
            _preedit.Text = _buffer.Length > 0 ? _buffer + "   ·   " + _preedit.Text : _preedit.Text;
            return;
        }

        if (_buffer.Length == 0)
        {
            _preedit.Text = _hanMode ? "漢  Gõ TELEX, ví dụ: hans vieetj" : "";
            _scroll.ContentOffset = CGPoint.Empty;
            LayoutCandidates();
            return;
        }

        _candidates = _engine.GetCandidates(_buffer).Take(MaxShown).ToList();
        _preedit.Text = _engine.RenderPreedit(_buffer, _candidates.FirstOrDefault());

        for (int i = 0; i < _candidates.Count; i++)
        {
            var cand = _candidates[i];
            var b = new UIButton(UIButtonType.Custom);
            b.SetTitle(cand.Text, UIControlState.Normal);
            b.SetTitleColor(i == 0 ? UIColor.Label : UIColor.SecondaryLabel, UIControlState.Normal);
            b.TitleLabel!.Font = UIFont.SystemFontOfSize(i == 0 ? 24 : 22);
            b.Layer.CornerRadius = 6;
            if (i == 0) b.BackgroundColor = Palette.Highlight;
            b.TouchUpInside += (_, _) => Commit(cand);
            _scroll.AddSubview(b);
            _candidateButtons.Add(b);
        }

        _scroll.ContentOffset = CGPoint.Empty;
        LayoutCandidates();
    }
}

/// <summary>Màu phím theo giao diện Sáng / Tối của bàn phím.</summary>
internal static class Palette
{
    public static readonly UIColor Key = new(t => t.UserInterfaceStyle == UIUserInterfaceStyle.Dark
        ? UIColor.FromRGB(0.42f, 0.42f, 0.42f)
        : UIColor.White);

    public static readonly UIColor SpecialKey = new(t => t.UserInterfaceStyle == UIUserInterfaceStyle.Dark
        ? UIColor.FromRGB(0.26f, 0.26f, 0.26f)
        : UIColor.FromRGB(0.67f, 0.70f, 0.74f));

    public static readonly UIColor Highlight = new(t => t.UserInterfaceStyle == UIUserInterfaceStyle.Dark
        ? UIColor.FromWhiteAlpha(1f, 0.15f)
        : UIColor.FromWhiteAlpha(0f, 0.08f));
}
