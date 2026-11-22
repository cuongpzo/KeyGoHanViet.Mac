using System.Reflection;

namespace KeyGoHanViet;

/// <summary>Thông tin phiên bản, lấy từ <c>Directory.Build.props</c> ở gốc kho mã.</summary>
public static class AppInfo
{
    /// <summary>Số phiên bản dạng "1.1.0".</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var asm = typeof(AppInfo).Assembly;
        string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // .NET gắn thêm "+<mã commit>" vào sau số phiên bản khi build trong kho git.
        if (info is { Length: > 0 }) return info.Split('+')[0];
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
