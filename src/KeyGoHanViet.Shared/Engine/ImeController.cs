using System.Text;
using KeyGoHanViet.Core;
using KeyGoHanViet.Platform;

namespace KeyGoHanViet.Engine;

/// <summary>Trạng thái bộ gõ được phát ra cho giao diện.</summary>
public sealed class ImeStateEventArgs : EventArgs
{
    public required bool Composing { get; init; }
    public required string RawInput { get; init; }
    public required string Preedit { get; init; }

    /// <summary>
    /// Đúng một trang ứng viên - phần đang nhìn thấy, không phải toàn bộ kho.
    ///
    /// Lớp giao diện vì thế cứ vẽ từ 0 tới hết như trước, còn số thứ tự 1-9 và
    /// cú nhấp chuột cũng tính theo chỉ số trong danh sách này.
    /// </summary>
    public required IReadOnlyList<Candidate> Candidates { get; init; }

    /// <summary>Ứng viên đang chọn, tính theo chỉ số <b>trong trang</b>.</summary>
    public required int SelectedIndex { get; init; }

    /// <summary>Số thứ tự trang đang xem, đếm từ 1.</summary>
    public int PageIndex { get; init; } = 1;

    /// <summary>Tổng số trang; bằng 1 khi mọi ứng viên vừa một trang.</summary>
    public int PageCount { get; init; } = 1;

    /// <summary>Tổng số ứng viên tra được, gộp hết mọi trang.</summary>
    public int TotalCandidates { get; init; }
}

/// <summary>Việc phải làm ngoài hàm xử lý hook - xem <see cref="ImeController.AsyncActionRequested"/>.</summary>
public enum ImeAsyncAction
{
    /// <summary>Đọc clipboard rồi tra ra chữ Hán Nôm.</summary>
    ClipboardLookup,
    /// <summary>Sao chép đoạn đang bôi đen ở ứng dụng đích rồi tra, chọn xong thì thay tại chỗ.</summary>
    ConvertSelection,
    /// <summary>Sao chép đoạn đang bôi đen rồi đọc âm Quốc ngữ của từng chữ Hán trong đó.</summary>
    HanToVietPopup,
}

/// <summary>
/// Máy trạng thái của bộ gõ: gom phím thành chuỗi đang gõ, tra gợi ý,
/// và xử lý các phím chức năng theo đúng §3 của đặc tả.
///
///   ↑ / ↓      duyệt danh sách gợi ý
///   Tab        lật sang trang gợi ý kế tiếp (Shift+Tab lật ngược)
///   Space      xác nhận chữ Hán Nôm đang chọn
///   Enter      huỷ gợi ý, xuất nguyên chuỗi Latin vừa gõ
///   Shift      chuyển nhanh giữa chế độ Hán Nôm và Latin
///   Esc        bỏ hẳn chuỗi đang gõ
///   1-9        chọn nhanh ứng viên theo số thứ tự
///
/// Bộ đệm chứa được cả hai dạng chuỗi và <see cref="CandidateEngine"/> tự nhận ra
/// đang là dạng nào:
///
///   TELEX thô   gõ từng phím a-z như thường
///   Quốc ngữ    "hán việt từ điển trích dẫn" - đến từ clipboard, từ vùng bôi
///               đen, hoặc do một bộ gõ Việt khác đang chạy sẵn bơm chữ có dấu vào
///
/// Lớp này không biết gì về hệ điều hành: nó nhận hook bàn phím, bộ xuất chữ và
/// clipboard từ bên ngoài, nên bản Windows và bản macOS dùng chung đúng một máy
/// trạng thái.
/// </summary>
public sealed class ImeController : IDisposable
{
    private readonly IKeyboardHook _hook;
    private readonly ITextOutput _output;
    private readonly IClipboard? _clipboard;
    private readonly StringBuilder _raw = new();

    private CandidateEngine _engine;
    private AppSettings _settings;

    /// <summary>
    /// Bảng tra ngược chữ Hán → âm Quốc ngữ, dựng khi dùng đến lần đầu.
    ///
    /// Dựng chậm chứ không dựng cùng bộ gõ: nó phải quét hết mấy vạn mục từ
    /// điển, mà người dùng có thể chẳng bao giờ bấm tới phím tắt này - không nên
    /// bắt mọi lần khởi động phải trả giá đó.
    /// </summary>
    private HanToVietTransliterator? _hanToViet;

    /// <summary>
    /// Toàn bộ ứng viên tra được cho chuỗi đang gõ, không chỉ phần nhìn thấy.
    ///
    /// Một âm như "tuwz" (tư) có tới mấy chục chữ đồng âm, mà khung gợi ý chỉ
    /// đủ chỗ cho chín dòng. Giữ cả kho ở đây rồi cắt trang lúc phát trạng thái
    /// ra giao diện, nhờ vậy phím Tab lật được tới chữ cuối cùng.
    /// </summary>
    private List<Candidate> _candidates = [];

    /// <summary>Ứng viên đang chọn, tính theo chỉ số trong <see cref="_candidates"/>.</summary>
    private int _selected;

    /// <summary>Chỉ số ứng viên đầu tiên của trang đang xem.</summary>
    private int _pageStart;

    /// <summary>
    /// true khi chuỗi đang tra là đoạn người dùng bôi đen ở ứng dụng đích. Xác
    /// nhận lúc đó là thay thế chứ không phải chèn thêm - vùng chọn vẫn còn nên
    /// chữ mới gửi ra sẽ đè lên nó.
    /// </summary>
    private bool _replacingSelection;

    /// <summary>
    /// true khi đã nhờ lớp nền tảng chạy một việc mà việc đó chưa xong.
    ///
    /// Giữ nguyên phím tắt thì bàn phím tự lặp hàng chục lần mỗi giây, mà mỗi
    /// lần lại đọc clipboard - riêng việc chuyển vùng bôi đen còn bơm thêm một
    /// phím sao chép nữa. Không chặn thì một cú giữ phím sinh ra cả tràng Ctrl+C
    /// gửi tới ứng dụng đích.
    /// </summary>
    private bool _asyncPending;

    // Theo dõi cú "gõ nhẹ" phím Shift để dùng làm phím chuyển chế độ.
    private bool _shiftPressedAlone;
    private bool _otherKeySinceShiftDown;

    /// <summary>Bảng nhớ chế độ K/Q của từng ứng dụng; null nếu lớp nền tảng không gắn vào.</summary>
    private AppModeStore? _appModes;

    private string _activeAppKey = "";
    private string _activeAppName = "";

    /// <summary>
    /// true khi chế độ đang đổi vì <b>vừa chuyển sang ứng dụng khác</b>, chứ
    /// không phải vì người dùng bấm phím chuyển.
    ///
    /// Không phân biệt hai chuyện đó thì việc khôi phục chế độ đã nhớ của ứng
    /// dụng mới lại tự ghi đè chính nó, và mốc "lần cuối đổi chế độ" trong
    /// config.json thành vô nghĩa vì nhảy theo mỗi lần bấm qua bấm lại cửa sổ.
    /// </summary>
    private bool _restoringAppMode;

    public event EventHandler<ImeStateEventArgs>? StateChanged;
    public event EventHandler? ModeChanged;

    /// <summary>
    /// Yêu cầu lớp nền tảng chạy một việc <b>ngoài</b> hàm xử lý hook, trên luồng
    /// giao diện: <see cref="RunClipboardLookup"/> hoặc <see cref="RunSelectionConversion"/>.
    ///
    /// Không làm thẳng tại chỗ vì hai việc đó đều phải đợi clipboard, mà hook
    /// chạy quá lâu thì Windows lẫn macOS đều lặng lẽ gỡ bỏ nó - bộ gõ chết câm
    /// mà không báo lỗi gì.
    /// </summary>
    public event EventHandler<ImeAsyncAction>? AsyncActionRequested;

    /// <summary>Kết quả vừa xuất ra - dùng cho ô thử nghiệm trong cửa sổ tuỳ chọn.</summary>
    public event EventHandler<string>? Committed;

    /// <summary>
    /// Đã đọc xong âm Quốc ngữ của đoạn đang bôi đen; lớp nền tảng hiện cửa sổ
    /// nhỏ cho người dùng đọc và sao chép. Phát ra trên luồng giao diện, ngay
    /// bên trong <see cref="RunHanToVietLookup"/>.
    /// </summary>
    public event EventHandler<HanVietResult>? HanToVietReady;

    public ImeController(CandidateEngine engine, AppSettings settings,
                         IKeyboardHook hook, ITextOutput output, IClipboard? clipboard = null)
    {
        _engine = engine;
        _settings = settings;
        _hook = hook;
        _output = output;
        _clipboard = clipboard;
        _hook.KeyEvent += OnKeyEvent;
    }

    public CandidateEngine Engine => _engine;
    public AppSettings Settings => _settings;
    public bool IsComposing => _raw.Length > 0;
    public string RawInput => _raw.ToString();
    /// <summary>Toàn bộ ứng viên tra được, gộp hết mọi trang.</summary>
    public IReadOnlyList<Candidate> Candidates => _candidates;

    /// <summary>Ứng viên đang chọn, tính trên toàn kho.</summary>
    public int SelectedIndex => _selected;

    /// <summary>Số dòng khung gợi ý hiện được cùng lúc, tức cỡ một trang.</summary>
    private int PageSize => Math.Max(1, _settings.MaxCandidates);

    /// <summary>Số trang của kho ứng viên hiện tại.</summary>
    public int PageCount => _candidates.Count == 0
        ? 1
        : (_candidates.Count + PageSize - 1) / PageSize;

    /// <summary>Số thứ tự trang đang xem, đếm từ 1.</summary>
    public int PageIndex => _pageStart / PageSize + 1;

    /// <summary>true = đang gõ Hán Nôm, false = gõ Latin bình thường.</summary>
    public bool HanNomMode
    {
        get => _settings.HanNomMode;
        set
        {
            if (_settings.HanNomMode == value) return;
            _settings.HanNomMode = value;
            if (!value) CancelComposition();
            if (!_restoringAppMode) RememberModeForActiveApp();
            ModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// true khi bộ gõ đang tạm nhường bàn phím cho chính chương trình này.
    ///
    /// Cửa sổ nhỏ hiện âm đọc có nhận tiêu điểm (phải nhận thì mới bôi đen và
    /// sao chép được). Hook bàn phím thì bắt phím của toàn hệ thống, kể cả phím
    /// gõ vào cửa sổ đó - không tạm ngưng thì gõ vào ô kết quả lại làm bật khung
    /// gợi ý của bộ gõ.
    /// </summary>
    public bool Suspended { get; private set; }

    /// <summary>Tạm ngưng xử lý phím; chuỗi đang gõ dở bị bỏ.</summary>
    public void Suspend()
    {
        if (Suspended) return;
        Suspended = true;
        CancelComposition();
    }

    public void Resume() => Suspended = false;

    public void Start() => _hook.Install();
    public void Stop() { CancelComposition(); _asyncPending = false; _hook.Uninstall(); }
    public bool IsRunning => _hook.IsInstalled;

    /// <summary>Thay bộ máy tra cứu sau khi người dùng nạp lại từ điển.</summary>
    public void SwapEngine(CandidateEngine engine, AppSettings settings)
    {
        CancelComposition();
        _engine = engine;
        _settings = settings;

        // Từ điển đã khác thì bảng tra ngược cũng phải dựng lại.
        _hanToViet = null;
    }

    /// <summary>Bảng tra ngược chữ Hán → âm Quốc ngữ của từ điển đang dùng.</summary>
    public HanToVietTransliterator HanToViet => _hanToViet ??= new HanToVietTransliterator(_engine.Dictionary);

    // ------------------------------------------------------------ xử lý phím

    private void OnKeyEvent(object? sender, HookedKeyEventArgs e)
    {
        if (!_settings.Enabled || Suspended) return;

        if (HandleToggleKey(e)) return;

        // Xem nhanh âm đọc không phải là việc gõ chữ, nên xét trước cả cổng chế
        // độ bên dưới: đang ở chế độ Latin (Q) mà gặp một đoạn chữ Hán thì vẫn
        // phải tra được, chứ bắt bật lại chế độ K rồi mới tra thì vô lý.
        if (e.IsKeyDown && HandleHanToVietHotkey(e)) return;

        if (!_settings.HanNomMode) return;      // chế độ Latin: để phím đi qua tự nhiên
        if (!e.IsKeyDown) return;

        if (HandleClipboardHotkeys(e)) return;

        // --- chữ Quốc ngữ có dấu do bộ gõ khác bơm vào --------------------
        // Xét trước nhánh phím bổ trợ ngay dưới, vì sự kiện bơm vào thường sinh
        // ra đúng lúc người dùng còn đang giữ Shift để gõ hoa. Ctrl và ⌘ thì
        // vẫn loại: giữ chúng là đang bấm lệnh chứ không phải gõ chữ, mà trên
        // macOS thì sự kiện phím lệnh vẫn chở theo ký tự của phím đó.
        if (_settings.EnableVietInput && !e.Control && !e.Win &&
            VietText.IsVietnameseLetter(e.Character))
        {
            _raw.Append(e.Character);
            Refresh();
            e.Handled = true;
            return;
        }

        if (e.Control || e.Alt || e.Win) { CommitRawIfComposing(); return; }

        var key = e.Key;

        // --- chữ cái a-z: nạp vào bộ đệm ---------------------------------
        if (key is >= VKey.A and <= VKey.Z)
        {
            char c = (char)('a' + (key - VKey.A));
            bool upper = e.Shift ^ e.CapsLock;
            _raw.Append(upper ? char.ToUpperInvariant(c) : c);
            Refresh();
            e.Handled = true;
            return;
        }

        if (!IsComposing)
        {
            // Chưa gõ gì thì mọi phím khác đi thẳng tới ứng dụng đích.
            return;
        }

        switch (key)
        {
            // --- duyệt danh sách gợi ý -----------------------------------
            case VKey.Down:
                MoveSelection(+1);
                e.Handled = true;
                return;

            case VKey.Up:
                MoveSelection(-1);
                e.Handled = true;
                return;

            case VKey.PageDown:
                MovePage(+1);
                e.Handled = true;
                return;

            case VKey.PageUp:
                MovePage(-1);
                e.Handled = true;
                return;

            // --- xác nhận chữ Hán Nôm đang chọn --------------------------
            case VKey.Space:
                if (_candidates.Count > 0)
                {
                    CommitCandidate(_selected);
                    e.Handled = true;
                }
                else
                {
                    CommitRaw();          // không có gợi ý: xuất Latin rồi để Space đi qua
                }
                return;

            // --- huỷ gợi ý, xuất nguyên chuỗi Latin ----------------------
            case VKey.Enter:
                CommitRaw();
                e.Handled = true;
                return;

            // --- bỏ hẳn chuỗi đang gõ ------------------------------------
            case VKey.Escape:
                CancelComposition();
                e.Handled = true;
                return;

            // --- xoá lùi từng ký tự --------------------------------------
            case VKey.Back:
                _raw.Length--;
                if (_raw.Length == 0) CancelComposition();
                else Refresh();
                e.Handled = true;
                return;

            case VKey.Tab:
                // Tab lật sang chín chữ kế tiếp, Shift+Tab lật ngược - giống
                // cách các bộ gõ Trung/Nhật cho xem hết danh sách chữ đồng âm.
                // Mũi tên ↑ ↓ vẫn dùng để nhích từng chữ một.
                MovePage(e.Shift ? -1 : +1);
                e.Handled = true;
                return;
        }

        // --- chọn nhanh bằng phím số 1-9 ---------------------------------
        if (_settings.SelectByDigit && _candidates.Count > 0)
        {
            int digit = DigitOf(key);

            // Số thứ tự đọc theo đúng con số in trên khung, tức là tính từ đầu
            // trang đang xem chứ không tính từ đầu kho: lật sang trang 2 rồi bấm
            // "1" thì phải ra chữ đầu của trang 2.
            if (digit >= 1 && digit <= 9 && digit <= PageLength)
            {
                CommitCandidate(_pageStart + digit - 1);
                e.Handled = true;
                return;
            }
        }

        // --- mọi phím khác: chốt Latin rồi để phím đó đi tiếp -------------
        CommitRaw();
    }

    private static int DigitOf(VKey key) => key switch
    {
        >= VKey.D0 and <= VKey.D9 => key - VKey.D0,
        >= VKey.NumPad0 and <= VKey.NumPad9 => key - VKey.NumPad0,
        _ => -1,
    };

    /// <summary>
    /// Xử lý phím chuyển chế độ. Với kiểu ShiftTap, chỉ chuyển khi Shift được
    /// nhấn rồi thả mà không có phím nào khác xen giữa - nhờ vậy Shift vẫn
    /// dùng để gõ chữ hoa bình thường.
    /// </summary>
    private bool HandleToggleKey(HookedKeyEventArgs e)
    {
        switch (_settings.ToggleKey)
        {
            case ToggleKeyKind.ShiftTap:
                if (e.Key is VKey.ShiftKey or VKey.LShiftKey or VKey.RShiftKey)
                {
                    if (e.IsKeyDown)
                    {
                        if (!_shiftPressedAlone) { _shiftPressedAlone = true; _otherKeySinceShiftDown = false; }
                    }
                    else
                    {
                        if (_shiftPressedAlone && !_otherKeySinceShiftDown) ToggleMode();
                        _shiftPressedAlone = false;
                    }
                    return false;   // không nuốt Shift, ứng dụng đích vẫn cần biết
                }
                if (e.IsKeyDown && _shiftPressedAlone) _otherKeySinceShiftDown = true;
                return false;

            case ToggleKeyKind.ControlShift:
                if (e.IsKeyDown && e.Control &&
                    e.Key is VKey.ShiftKey or VKey.LShiftKey or VKey.RShiftKey)
                {
                    ToggleMode();
                    e.Handled = true;
                    return true;
                }
                return false;

            case ToggleKeyKind.AltZ:
                if (e.IsKeyDown && e.Alt && e.Key == VKey.Z) { ToggleMode(); e.Handled = true; return true; }
                return false;

            case ToggleKeyKind.ControlSpace:
                if (e.IsKeyDown && e.Control && e.Key == VKey.Space) { ToggleMode(); e.Handled = true; return true; }
                return false;

            case ToggleKeyKind.Custom:
                if (e.IsKeyDown &&
                    HotkeySpec.Matches(CustomHotkey, e.Key, e.Control, e.Alt, e.Shift))
                {
                    ToggleMode();
                    e.Handled = true;
                    return true;
                }
                return false;

            default:
                return false;
        }
    }

    // ------------------------------------------------ tra clipboard / vùng chọn

    /// <summary>
    /// Bắt hai phím tắt tra chữ từ bên ngoài. Chỉ đánh dấu là đã xử lý rồi nhờ
    /// lớp nền tảng làm tiếp - xem <see cref="AsyncActionRequested"/>.
    /// </summary>
    private bool HandleClipboardHotkeys(HookedKeyEventArgs e)
    {
        if (_clipboard is null || !_settings.EnableVietInput) return false;

        if (Matches(_settings.ClipboardLookupHotkey, e))
            return Request(e, ImeAsyncAction.ClipboardLookup);

        if (Matches(_settings.ConvertSelectionHotkey, e))
            return Request(e, ImeAsyncAction.ConvertSelection);

        return false;
    }

    /// <summary>Bắt phím tắt xem nhanh âm đọc Quốc ngữ của đoạn đang bôi đen.</summary>
    private bool HandleHanToVietHotkey(HookedKeyEventArgs e)
    {
        if (_clipboard is null || !_settings.EnableHanToViet) return false;
        if (!Matches(_settings.HanToVietHotkey, e)) return false;

        return Request(e, ImeAsyncAction.HanToVietPopup);
    }

    private bool Request(HookedKeyEventArgs e, ImeAsyncAction action)
    {
        // Vẫn nuốt phím kể cả khi đang bận: để lọt qua thì ứng dụng đích nhận
        // được nửa tổ hợp và làm chuyện của nó (Ctrl+Shift+V ở nhiều trình soạn
        // thảo là "dán không định dạng").
        e.Handled = true;

        // Không ai lắng nghe thì đừng bật cờ: bật rồi mà chẳng có ai gọi
        // Run* để tắt đi thì phím tắt chết hẳn từ lần bấm đầu tiên.
        var handler = AsyncActionRequested;
        if (handler is null || _asyncPending) return true;

        _asyncPending = true;
        handler(this, action);
        return true;
    }

    private static bool Matches(string? spec, HookedKeyEventArgs e) =>
        !string.IsNullOrWhiteSpace(spec) &&
        HotkeySpec.Matches(HotkeySpec.Parse(spec), e.Key, e.Control, e.Alt, e.Shift);

    /// <summary>
    /// Đọc clipboard rồi mở khung gợi ý cho nội dung đó. Chọn xong thì chữ Hán
    /// Nôm được chèn vào chỗ con trỏ đang đứng.
    ///
    /// Phải gọi trên luồng giao diện, không gọi trong hàm xử lý hook.
    /// </summary>
    public void RunClipboardLookup()
    {
        try
        {
            if (_clipboard is null) return;
            BeginExternalComposition(_clipboard.GetText(), replacingSelection: false);
        }
        finally { _asyncPending = false; }
    }

    /// <summary>
    /// Sao chép đoạn đang bôi đen ở ứng dụng đích rồi mở khung gợi ý cho nó.
    /// Chọn xong thì chữ Hán Nôm thay thẳng vào chỗ đoạn đó.
    ///
    /// Phải gọi trên luồng giao diện, không gọi trong hàm xử lý hook.
    /// </summary>
    public void RunSelectionConversion()
    {
        try
        {
            if (_clipboard is null) return;
            BeginExternalComposition(_clipboard.CopySelection(), replacingSelection: true);
        }
        finally { _asyncPending = false; }
    }

    /// <summary>
    /// Sao chép đoạn đang bôi đen ở ứng dụng đích rồi đọc âm Quốc ngữ của từng
    /// chữ Hán Nôm trong đó, phát ra qua <see cref="HanToVietReady"/>.
    ///
    /// Khác hai hàm trên ở chỗ: không đụng gì tới văn bản của ứng dụng đích và
    /// không mở khung gợi ý - chỉ đọc rồi đưa kết quả cho lớp giao diện hiện lên.
    ///
    /// Phải gọi trên luồng giao diện, không gọi trong hàm xử lý hook.
    /// </summary>
    public void RunHanToVietLookup()
    {
        try
        {
            if (_clipboard is null) return;

            string? selection = _clipboard.CopySelection();

            // Không bôi đen gì (hoặc ứng dụng đích không chịu sao chép): im lặng
            // bỏ qua, hiện một cửa sổ trống chỉ làm người dùng bối rối.
            if (string.IsNullOrWhiteSpace(selection)) return;

            var result = HanToViet.Convert(selection.Trim());
            HanToVietReady?.Invoke(this, result);
        }
        finally { _asyncPending = false; }
    }

    /// <summary>
    /// Số ký tự tối đa nhận từ clipboard. Cắt ngưỡng vì mục dài nhất trong từ
    /// điển chỉ vài chục ký tự, mà bấm nhầm phím tắt lúc clipboard đang giữ cả
    /// một trang văn bản thì không nên để bộ gõ ngồi tra hết chỗ đó.
    /// </summary>
    private const int MaxExternalLength = 120;

    private void BeginExternalComposition(string? text, bool replacingSelection)
    {
        if (string.IsNullOrWhiteSpace(text)) { CancelComposition(); return; }

        string trimmed = text.Trim();
        if (trimmed.Length > MaxExternalLength) { CancelComposition(); return; }

        ResetComposition();
        _raw.Append(trimmed);
        _replacingSelection = replacingSelection;

        Refresh();

        // Không tra được gì thì đừng để khung gợi ý treo lơ lửng với mỗi chuỗi
        // gốc: người dùng sẽ tưởng bộ gõ đang chờ mình bấm tiếp.
        if (_candidates.Count == 0) CancelComposition();
    }

    public void ToggleMode() => HanNomMode = !HanNomMode;

    // ------------------------------------------------ chế độ theo từng ứng dụng

    /// <summary>Gắn bảng nhớ chế độ của từng ứng dụng. Gọi một lần lúc khởi động.</summary>
    public void AttachAppModes(AppModeStore store) => _appModes = store;

    /// <summary>Bảng nhớ đang dùng, để cửa sổ tuỳ chọn hiện danh sách ra xem.</summary>
    public AppModeStore? AppModes => _appModes;

    /// <summary>Khoá của ứng dụng đang ở trước ("chrome"), rỗng nếu chưa biết.</summary>
    public string ActiveAppKey => _activeAppKey;

    /// <summary>Tên dễ đọc của ứng dụng đang ở trước.</summary>
    public string ActiveAppName => _activeAppName;

    /// <summary>
    /// Lớp nền tảng báo rằng ứng dụng ở trước vừa đổi.
    ///
    /// Đây là chỗ thực hiện đúng cái mà người dùng mong đợi: bật chữ Hán ở
    /// Chrome thì chỉ Chrome gõ ra chữ Hán, mọi cửa sổ khác vẫn gõ Latin cho tới
    /// khi cũng được bật riêng. Chuyển qua lại giữa hai cửa sổ đã bật thì cả hai
    /// đều giữ chữ Hán, vì chế độ đọc lại từ bảng nhớ chứ không dùng chung.
    ///
    /// Gọi cả khi <see cref="AppSettings.PerAppMode"/> đang tắt: lúc đó chế độ
    /// vẫn dùng chung toàn hệ thống, nhưng danh sách ứng dụng vẫn được ghi tiếp
    /// để bật tính năng lên là có sẵn nhật ký.
    /// </summary>
    public void NotifyActiveApp(string key, string name)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        bool sameApp = string.Equals(key, _activeAppKey, StringComparison.OrdinalIgnoreCase);
        _activeAppKey = key;
        _activeAppName = name;

        var store = _appModes;
        if (store is null) return;

        // Ứng dụng cũ quay lại tiêu điểm (đóng hộp thoại, bấm ra bấm vào): chỉ
        // cập nhật mốc thời gian, không đụng gì tới chuỗi đang gõ dở.
        if (sameApp)
        {
            store.Touch(key, name);
            return;
        }

        bool isNew = store.Touch(key, name);

        // Chuỗi gõ dở thuộc về cửa sổ cũ. Mang nó theo sang cửa sổ mới thì chữ
        // xuất ra sẽ rơi nhầm chỗ, nên bỏ đi cùng với khung gợi ý.
        CancelComposition();

        if (_settings.PerAppMode)
        {
            bool want = store.GetMode(key);
            if (want != _settings.HanNomMode)
            {
                _restoringAppMode = true;
                try { HanNomMode = want; }
                finally { _restoringAppMode = false; }
            }
        }

        // Ứng dụng mới toanh thì ghi ngay để danh sách trong config.json không
        // trễ một phiên so với thực tế; ứng dụng cũ chỉ đổi mốc "lần cuối thấy",
        // để dành ghi lúc thoát cho khỏi đụng đĩa trên mỗi cú Alt+Tab.
        if (isNew) store.Save();
    }

    /// <summary>Ghi chế độ vừa chọn vào đúng ứng dụng đang ở trước.</summary>
    private void RememberModeForActiveApp()
    {
        if (!_settings.PerAppMode) return;

        var store = _appModes;
        if (store is null || _activeAppKey.Length == 0) return;

        if (store.SetMode(_activeAppKey, _activeAppName, _settings.HanNomMode))
            store.Save();
    }

    private string _hotkeyCacheKey = "";
    private VKey _hotkeyCacheValue = VKey.None;

    /// <summary>
    /// Tổ hợp phím tắt đã phân tích sẵn. Có nhớ đệm vì thuộc tính này được đọc
    /// trong hook bàn phím, chạy trên từng phím của toàn hệ thống.
    /// </summary>
    private VKey CustomHotkey
    {
        get
        {
            string text = _settings.CustomToggleKey ?? "";
            if (!string.Equals(text, _hotkeyCacheKey, StringComparison.Ordinal))
            {
                _hotkeyCacheKey = text;
                _hotkeyCacheValue = HotkeySpec.Parse(text);
            }
            return _hotkeyCacheValue;
        }
    }

    // ------------------------------------------------------------ trạng thái

    /// <summary>Nhích ô chọn đi <paramref name="delta"/> ứng viên, cuộn vòng ở hai đầu.</summary>
    private void MoveSelection(int delta)
    {
        if (_candidates.Count == 0) return;
        int n = _candidates.Count;
        _selected = ((_selected + delta) % n + n) % n;

        // Ô chọn đi khỏi trang thì trang chạy theo, nhờ vậy giữ ↓ một lúc là
        // xem được cả kho mà không cần đụng tới phím Tab.
        _pageStart = _selected / PageSize * PageSize;
        RaiseState();
    }

    /// <summary>
    /// Lật sang trang khác và đưa ô chọn về dòng đầu trang mới.
    ///
    /// Lật quá trang cuối thì quay lại trang đầu chứ không đứng im: người dùng
    /// đang bấm Tab liên tục để dò, dừng câm ở trang cuối trông như bộ gõ treo.
    /// </summary>
    private void MovePage(int delta)
    {
        if (_candidates.Count == 0) return;

        int pages = PageCount;

        // Cả danh sách vừa đúng một trang: không có gì để lật, nên Tab quay về
        // đúng nghĩa cũ là nhích từng chữ. Lật trang lúc này chỉ làm ô chọn
        // nhảy ngược lên dòng đầu, tệ hơn hẳn so với trước.
        if (pages == 1) { MoveSelection(delta >= 0 ? +1 : -1); return; }

        int page = ((PageIndex - 1 + delta) % pages + pages) % pages;

        _pageStart = page * PageSize;
        _selected = _pageStart;
        RaiseState();
    }

    /// <summary>Số ứng viên thật sự có trên trang đang xem (trang cuối thường thiếu).</summary>
    private int PageLength => Math.Max(0, Math.Min(PageSize, _candidates.Count - _pageStart));

    /// <summary>Đúng một trang ứng viên, phần mà khung gợi ý đang hiện.</summary>
    private IReadOnlyList<Candidate> CurrentPage =>
        _candidates.GetRange(_pageStart, PageLength);

    private void Refresh()
    {
        _candidates = _engine.GetCandidates(_raw.ToString());
        _selected = 0;
        _pageStart = 0;
        RaiseState();
    }

    /// <summary>
    /// Xác nhận ứng viên theo chỉ số <b>trong trang đang xem</b> - dùng khi người
    /// dùng bấm chuột vào một dòng của khung gợi ý.
    /// </summary>
    public void CommitByIndex(int index) => CommitCandidate(_pageStart + index);

    private void CommitCandidate(int index)
    {
        if (index < 0 || index >= _candidates.Count) { CommitRaw(); return; }

        var chosen = _candidates[index];
        string input = _raw.ToString();

        _engine.RecordChoice(input, chosen.Text);

        string output = chosen.Text;

        // Thay đoạn bôi đen thì không chèn thêm dấu cách: người dùng đang sửa
        // chữ có sẵn giữa câu, thêm dấu cách là làm hỏng câu đó.
        if (_settings.AppendSpaceAfterCommit && !_replacingSelection) output += " ";

        ResetComposition();
        Send(output);
    }

    private void CommitRaw()
    {
        if (_raw.Length == 0) return;

        // Đang chuyển đoạn bôi đen mà người dùng đổi ý: bỏ qua, giữ nguyên văn
        // bản trong ứng dụng đích. Gửi lại đúng chuỗi cũ thì nhìn thì không đổi
        // gì, nhưng lịch sử hoàn tác của ứng dụng lại có thêm một bước vô nghĩa.
        if (_replacingSelection) { CancelComposition(); return; }

        string text = _raw.ToString();
        ResetComposition();
        Send(text);
    }

    private void CommitRawIfComposing()
    {
        if (IsComposing) CommitRaw();
    }

    /// <summary>Bỏ chuỗi đang gõ mà không xuất gì cả.</summary>
    public void CancelComposition()
    {
        if (_raw.Length == 0 && _candidates.Count == 0) return;
        ResetComposition();
        RaiseState();
    }

    private void ResetComposition()
    {
        _raw.Clear();
        _candidates = [];
        _selected = 0;
        _pageStart = 0;
        _replacingSelection = false;
    }

    private void Send(string text)
    {
        RaiseState();

        bool ok = _settings.Output == OutputMethod.ClipboardPaste
            ? _output.SendViaClipboard(text)
            : _output.SendUnicode(text);

        // Gửi phím Unicode thất bại (ứng dụng đích chạy quyền cao hơn) -> thử clipboard.
        if (!ok && _settings.Output == OutputMethod.SendInputUnicode)
            _output.SendViaClipboard(text);

        Committed?.Invoke(this, text);
    }

    private void RaiseState() => StateChanged?.Invoke(this, new ImeStateEventArgs
    {
        Composing = IsComposing,
        RawInput = _raw.ToString(),
        Preedit = _raw.Length > 0
            ? _engine.RenderPreedit(_raw.ToString(),
                  _selected < _candidates.Count ? _candidates[_selected] : null)
            : "",
        Candidates = CurrentPage,
        SelectedIndex = _selected - _pageStart,
        PageIndex = PageIndex,
        PageCount = PageCount,
        TotalCandidates = _candidates.Count,
    });

    public void Dispose()
    {
        _hook.KeyEvent -= OnKeyEvent;
        _hook.Dispose();
    }
}
