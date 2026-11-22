using System.Buffers.Binary;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace KeyGoHanViet.Mac.UI;

/// <summary>
/// Nạp hai biểu tượng thanh trình đơn: K = đang gõ Hán Nôm, Q = đã tắt.
/// Bản macOS của <c>UI\TrayIcons.cs</c> bên Windows.
///
/// Dùng chung đúng hai tệp .ico với bản Windows để hai bản không bao giờ lệch
/// hình. Nhưng Avalonia không giải mã được định dạng .ico, nên lớp này tự đọc
/// bảng thư mục của tệp rồi bóc lấy khối PNG nằm bên trong - hai tệp .ico này
/// chỉ chứa đúng một lớp ảnh 256×256 nén PNG, nên bóc ra là dùng được ngay.
/// Đây cũng chính là cách bản Windows đang làm, vì GDI+ cũng không đọc được
/// lớp PNG trong .ico.
/// </summary>
internal static class MacTrayIcons
{
    /// <summary>
    /// Cỡ vẽ ra cho thanh trình đơn. macOS dành khoảng 22 điểm chiều cao; lấy
    /// gấp đôi để màn Retina vẫn sắc nét.
    /// </summary>
    private static readonly Avalonia.PixelSize TraySize = new(44, 44);

    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static WindowIcon? _on;
    private static WindowIcon? _off;
    private static WindowIcon? _app;

    /// <summary>Biểu tượng K - bộ gõ đang bật.</summary>
    internal static WindowIcon? On => _on ??= Load("KeyGoHanViet.Icons.On.ico", TraySize);

    /// <summary>Biểu tượng Q - bộ gõ đã tắt.</summary>
    internal static WindowIcon? Off => _off ??= Load("KeyGoHanViet.Icons.Off.ico", TraySize);

    /// <summary>Biểu tượng chung của chương trình, dùng cho cửa sổ tuỳ chọn.</summary>
    internal static WindowIcon? App => _app ??= Load("KeyGoHanViet.Icons.On.ico", new Avalonia.PixelSize(128, 128));

    private static WindowIcon? Load(string resourceName, Avalonia.PixelSize size)
    {
        try
        {
            byte[]? png = ExtractLargestPng(resourceName);
            if (png is null) return null;

            using var stream = new MemoryStream(png);
            using var full = new Bitmap(stream);
            using var scaled = full.CreateScaledBitmap(size, BitmapInterpolationMode.HighQuality);

            return new WindowIcon(scaled);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            // Thiếu biểu tượng thì thanh trình đơn hiện ô trống, nhưng bộ gõ
            // vẫn chạy - không đáng để làm sập chương trình.
            return null;
        }
    }

    /// <summary>
    /// Đọc bảng thư mục của tệp .ico và trả về khối PNG lớn nhất bên trong.
    /// Trả null nếu tệp không chứa lớp nào nén PNG.
    /// </summary>
    private static byte[]? ExtractLargestPng(string resourceName)
    {
        using Stream? resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (resource is null) return null;

        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        byte[] ico = buffer.ToArray();

        // ICONDIR: 2 byte dự trữ, 2 byte kiểu, 2 byte số lớp ảnh.
        const int HeaderSize = 6;
        const int EntrySize = 16;
        if (ico.Length < HeaderSize) return null;

        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4, 2));
        if (count == 0 || ico.Length < HeaderSize + count * EntrySize) return null;

        byte[]? best = null;

        for (int i = 0; i < count; i++)
        {
            // ICONDIRENTRY: 12 byte mô tả, rồi 4 byte kích thước và 4 byte vị trí.
            int entry = HeaderSize + i * EntrySize;
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(entry + 8, 4));
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(entry + 12, 4));

            if (length <= 0 || offset < 0 || offset + length > ico.Length) continue;

            var payload = ico.AsSpan(offset, length);
            if (!payload.StartsWith(PngSignature)) continue;   // lớp ảnh dạng BMP cũ - bỏ qua

            if (best is null || length > best.Length) best = payload.ToArray();
        }

        return best;
    }
}
