using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Xml;

namespace KeyGoHanViet.Mac.Interop;

/// <summary>
/// Đăng ký / huỷ đăng ký tự chạy khi đăng nhập - bản macOS của
/// <c>StartupRegistration.cs</c> bên Windows.
///
/// Windows ghi một giá trị vào khoá Run của registry; macOS đặt một tệp
/// LaunchAgent trong thư mục riêng của người dùng. Cả hai đều không cần quyền
/// quản trị và chỉ ảnh hưởng tới tài khoản đang dùng.
/// </summary>
public static class MacStartupRegistration
{
    private const string Label = "com.cuongpzo.keygohanviet";

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", Label + ".plist");

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint getuid();

    public static bool IsRegistered() => File.Exists(PlistPath);

    public static void Apply(bool enable)
    {
        try
        {
            if (enable) Register();
            else Unregister();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Chính sách máy chặn ghi tệp - bỏ qua, không làm gián đoạn người dùng.
        }
    }

    private static void Register()
    {
        string exe = Environment.ProcessPath ?? "";
        if (exe.Length == 0) return;

        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        File.WriteAllText(PlistPath, BuildPlist(exe));

        // Nạp ngay để không phải đăng xuất rồi đăng nhập lại mới thấy tác dụng.
        // Thất bại cũng không sao: tệp đã nằm đúng chỗ nên lần đăng nhập sau
        // launchd vẫn tự chạy chương trình.
        RunLaunchctl("bootstrap", $"gui/{getuid()}", PlistPath);
    }

    private static void Unregister()
    {
        if (!File.Exists(PlistPath)) return;

        RunLaunchctl("bootout", $"gui/{getuid()}/{Label}");
        File.Delete(PlistPath);
    }

    /// <summary>
    /// Dựng tệp plist bằng bộ ghi XML thay vì nối chuỗi: đường dẫn tới gói .app
    /// do người dùng đặt tên, hoàn toàn có thể chứa &amp; hoặc &lt; và nối chuỗi
    /// thì sinh ra tệp hỏng mà launchd lặng lẽ bỏ qua.
    /// </summary>
    private static string BuildPlist(string executablePath)
    {
        var buffer = new StringWriter();
        var settings = new XmlWriterSettings { Indent = true, Encoding = System.Text.Encoding.UTF8 };

        using (var w = XmlWriter.Create(buffer, settings))
        {
            w.WriteStartDocument();
            w.WriteDocType("plist", "-//Apple//DTD PLIST 1.0//EN",
                           "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null);
            w.WriteStartElement("plist");
            w.WriteAttributeString("version", "1.0");
            w.WriteStartElement("dict");

            w.WriteElementString("key", "Label");
            w.WriteElementString("string", Label);

            w.WriteElementString("key", "ProgramArguments");
            w.WriteStartElement("array");
            w.WriteElementString("string", executablePath);
            w.WriteEndElement();

            w.WriteElementString("key", "RunAtLoad");
            w.WriteStartElement("true");
            w.WriteEndElement();

            // Bộ gõ nằm ở thanh trình đơn, không có cửa sổ nào - đừng để launchd
            // bật lại liên tục nếu người dùng chủ động thoát chương trình.
            w.WriteElementString("key", "KeepAlive");
            w.WriteStartElement("false");
            w.WriteEndElement();

            w.WriteEndElement();   // dict
            w.WriteEndElement();   // plist
            w.WriteEndDocument();
        }

        return buffer.ToString();
    }

    private static void RunLaunchctl(params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo("/bin/launchctl")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string a in args) info.ArgumentList.Add(a);

            using var p = Process.Start(info);
            p?.WaitForExit(3000);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // launchctl không gọi được - tệp plist vẫn đúng chỗ, lần đăng nhập sau sẽ ăn.
        }
    }
}
