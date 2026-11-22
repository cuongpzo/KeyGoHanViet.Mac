using Avalonia;

namespace KeyGoHanViet.Mac;

internal static class Program
{
    /// <summary>
    /// Giữ tệp khoá mở suốt đời chương trình. Đóng lại là mất khoá, nên đây là
    /// trường tĩnh chứ không phải biến cục bộ trong <see cref="Main"/>.
    /// </summary>
    private static FileStream? _instanceLock;

    [STAThread]
    private static int Main(string[] args)
    {
        // Chỉ cho phép một bản chạy: bộ bắt phím toàn cục không nên bị nhân đôi.
        //
        // Không dùng Mutex đặt tên như bản Windows được - trên macOS, Mutex có
        // tên của .NET chỉ có tác dụng trong phạm vi một tiến trình. Khoá theo
        // tệp thì đúng ở cả hai hệ, và hệ điều hành tự thả khoá khi tiến trình
        // chết, kể cả chết vì lỗi.
        if (!TryAcquireInstanceLock())
        {
            Console.Error.WriteLine(
                "KeyGo Hán Việt đang chạy rồi. Hãy tìm biểu tượng K ở thanh trình đơn trên cùng.");
            return 1;
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            ReportCrash(ex);
            return 1;
        }
    }

    /// <summary>
    /// Chọn thẳng backend macOS thay vì <c>UsePlatformDetect()</c>: hàm dò tự
    /// động nằm trong gói gộp Avalonia.Desktop, mà gói đó kéo theo cả backend
    /// Win32 lẫn X11 - xem ghi chú trong tệp .csproj.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UseAvaloniaNative()
                  .UseSkia()
                  .WithInterFont()
                  .LogToTrace();

    private static bool TryAcquireInstanceLock()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ProfileDir);
            _instanceLock = new FileStream(
                Path.Combine(AppPaths.ProfileDir, "instance.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;      // một bản khác đang giữ khoá
        }
        catch (UnauthorizedAccessException)
        {
            return true;       // không khoá được thì cứ chạy, còn hơn không chạy được
        }
    }

    private static void ReportCrash(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ProfileDir);
            File.AppendAllText(Path.Combine(AppPaths.ProfileDir, "crash.log"),
                $"[{DateTime.Now:u}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException) { }

        Console.Error.WriteLine($"Đã xảy ra lỗi: {ex.Message}");
    }
}
