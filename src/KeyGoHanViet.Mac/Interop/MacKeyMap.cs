using KeyGoHanViet.Core;

namespace KeyGoHanViet.Mac.Interop;

/// <summary>
/// Ánh xạ mã phím ảo của macOS (CGKeyCode) sang <see cref="VKey"/>.
///
/// Bản Windows không cần bảng này vì <c>VKey</c> vốn dùng luôn giá trị mã phím
/// ảo của Windows. macOS thì đánh số hoàn toàn khác - mã của nó bám theo *vị trí
/// vật lý* của phím trên bàn phím Apple đời đầu chứ không theo chữ cái, nên
/// phím A là 0, S là 1, D là 2... Vì vậy phải tra bảng.
///
/// Giá trị lấy từ <c>Carbon/HIToolbox/Events.h</c> (các hằng <c>kVK_*</c>).
/// Bảng bám theo bố cục vật lý, không theo bố cục bàn phím người dùng đang
/// chọn - đúng như ý muốn ở đây, vì chuỗi TELEX được gõ bằng phím chữ Latin và
/// bộ gõ cần biết người dùng bấm phím nào chứ không cần biết nó sinh ra chữ gì.
/// </summary>
internal static class MacKeyMap
{
    /// <summary>Mã phím dùng khi bơm tổ hợp dán (⌘V) và sao chép (⌘C) ra ngoài.</summary>
    internal const ushort kVK_ANSI_V = 0x09;
    internal const ushort kVK_ANSI_C = 0x08;

    private static readonly VKey[] Table = BuildTable();

    /// <summary>Trả <see cref="VKey.None"/> nếu mã phím không có trong bảng.</summary>
    internal static VKey ToVKey(long macKeyCode) =>
        macKeyCode >= 0 && macKeyCode < Table.Length ? Table[macKeyCode] : VKey.None;

    private static VKey[] BuildTable()
    {
        var t = new VKey[128];

        // --- hàng chữ cái, theo vị trí vật lý trên bàn phím Apple -----------
        t[0x00] = VKey.A;
        t[0x01] = VKey.S;
        t[0x02] = VKey.D;
        t[0x03] = VKey.F;
        t[0x04] = VKey.H;
        t[0x05] = VKey.G;
        t[0x06] = VKey.Z;
        t[0x07] = VKey.X;
        t[0x08] = VKey.C;
        t[0x09] = VKey.V;
        t[0x0B] = VKey.B;
        t[0x0C] = VKey.Q;
        t[0x0D] = VKey.W;
        t[0x0E] = VKey.E;
        t[0x0F] = VKey.R;
        t[0x10] = VKey.Y;
        t[0x11] = VKey.T;
        t[0x1F] = VKey.O;
        t[0x20] = VKey.U;
        t[0x22] = VKey.I;
        t[0x23] = VKey.P;
        t[0x25] = VKey.L;
        t[0x26] = VKey.J;
        t[0x28] = VKey.K;
        t[0x2D] = VKey.N;
        t[0x2E] = VKey.M;

        // --- hàng số -------------------------------------------------------
        t[0x12] = VKey.D1;
        t[0x13] = VKey.D2;
        t[0x14] = VKey.D3;
        t[0x15] = VKey.D4;
        t[0x16] = VKey.D6;   // 6 và 5 đảo thứ tự so với trực giác, đúng theo Events.h
        t[0x17] = VKey.D5;
        t[0x19] = VKey.D9;
        t[0x1A] = VKey.D7;
        t[0x1C] = VKey.D8;
        t[0x1D] = VKey.D0;

        // --- phím dấu ------------------------------------------------------
        t[0x18] = VKey.Oemplus;            // =
        t[0x1B] = VKey.OemMinus;           // -
        t[0x1E] = VKey.OemCloseBrackets;   // ]
        t[0x21] = VKey.OemOpenBrackets;    // [
        t[0x27] = VKey.OemQuotes;          // '
        t[0x29] = VKey.OemSemicolon;       // ;
        t[0x2A] = VKey.OemPipe;            // \
        t[0x2B] = VKey.Oemcomma;           // ,
        t[0x2C] = VKey.OemQuestion;        // /
        t[0x2F] = VKey.OemPeriod;          // .
        t[0x32] = VKey.Oemtilde;           // `

        // --- phím điều khiển ------------------------------------------------
        t[0x24] = VKey.Enter;
        t[0x30] = VKey.Tab;
        t[0x31] = VKey.Space;
        t[0x33] = VKey.Back;      // phím Delete của Mac chính là Backspace
        t[0x35] = VKey.Escape;
        t[0x72] = VKey.Help;
        t[0x73] = VKey.Home;
        t[0x74] = VKey.PageUp;
        t[0x75] = VKey.Delete;    // fn+Delete = Forward Delete
        t[0x77] = VKey.End;
        t[0x79] = VKey.PageDown;
        t[0x7B] = VKey.Left;
        t[0x7C] = VKey.Right;
        t[0x7D] = VKey.Down;
        t[0x7E] = VKey.Up;

        // --- phím bổ trợ ----------------------------------------------------
        // macOS báo các phím này qua sự kiện FlagsChanged chứ không phải KeyDown;
        // MacKeyboardHook tự dựng lại cặp nhấn/thả cho chúng.
        t[0x37] = VKey.LWin;          // ⌘ trái
        t[0x36] = VKey.RWin;          // ⌘ phải
        t[0x38] = VKey.LShiftKey;
        t[0x3C] = VKey.RShiftKey;
        t[0x39] = VKey.Capital;       // CapsLock
        t[0x3A] = VKey.LMenu;         // ⌥ trái  (Option = Alt)
        t[0x3D] = VKey.RMenu;         // ⌥ phải
        t[0x3B] = VKey.LControlKey;
        t[0x3E] = VKey.RControlKey;

        // --- bàn phím số ----------------------------------------------------
        t[0x41] = VKey.Decimal;
        t[0x43] = VKey.Multiply;
        t[0x45] = VKey.Add;
        t[0x47] = VKey.NumLock;    // phím Clear của Mac nằm đúng chỗ NumLock
        t[0x4B] = VKey.Divide;
        t[0x4C] = VKey.Enter;      // Enter của bàn phím số
        t[0x4E] = VKey.Subtract;
        t[0x51] = VKey.Oemplus;    // = của bàn phím số
        t[0x52] = VKey.NumPad0;
        t[0x53] = VKey.NumPad1;
        t[0x54] = VKey.NumPad2;
        t[0x55] = VKey.NumPad3;
        t[0x56] = VKey.NumPad4;
        t[0x57] = VKey.NumPad5;
        t[0x58] = VKey.NumPad6;
        t[0x59] = VKey.NumPad7;
        t[0x5B] = VKey.NumPad8;
        t[0x5C] = VKey.NumPad9;

        // --- phím chức năng --------------------------------------------------
        t[0x7A] = VKey.F1;
        t[0x78] = VKey.F2;
        t[0x63] = VKey.F3;
        t[0x76] = VKey.F4;
        t[0x60] = VKey.F5;
        t[0x61] = VKey.F6;
        t[0x62] = VKey.F7;
        t[0x64] = VKey.F8;
        t[0x65] = VKey.F9;
        t[0x6D] = VKey.F10;
        t[0x67] = VKey.F11;
        t[0x6F] = VKey.F12;
        t[0x69] = VKey.F13;
        t[0x6B] = VKey.F14;
        t[0x71] = VKey.F15;
        t[0x6A] = VKey.F16;
        t[0x40] = VKey.F17;
        t[0x4F] = VKey.F18;
        t[0x50] = VKey.F19;
        t[0x5A] = VKey.F20;

        return t;
    }
}
