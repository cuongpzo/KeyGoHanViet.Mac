using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using KeyGoHanViet.Core;
using KeyGoHanViet.Engine;
using KeyGoHanViet.Mac.Interop;
using KeyGoHanViet.Mac.UI;

namespace KeyGoHanViet.Mac;

/// <summary>
/// Vòng đời ứng dụng: giữ biểu tượng thanh trình đơn, bộ gõ, khung gợi ý và cửa
/// sổ tuỳ chọn. Bản macOS của <c>UI\TrayApp.cs</c> bên Windows.
/// </summary>
public sealed class App : Application
{
    private AppSettings _settings = null!;
    private HanNomDictionary _dict = null!;
    private MacroStore _macros = null!;
    private UsageMemory _usage = null!;
    private CandidateEngine _engine = null!;

    private ImeController _ime = null!;
    private CandidateWindow _popup = null!;
    private OptionsWindow? _options;

    /// <summary>Cửa sổ nhỏ hiện âm đọc Quốc ngữ; chỉ sống khi đang được nhìn tới.</summary>
    private HanVietPopupWindow? _hanVietPopup;

    /// <summary>Chế độ K/Q đã nhớ cho từng ứng dụng, lưu ở config.json.</summary>
    private AppModeStore _appModes = null!;

    /// <summary>Theo dõi ứng dụng nào đang ở trước, để đổi chế độ theo nó.</summary>
    private MacActiveAppWatcher _appWatcher = null!;

    private TrayIcon _tray = null!;
    private NativeMenuItem _miMode = null!;
    private NativeMenuItem _miEnabled = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Chương trình sống ở thanh trình đơn, không có cửa sổ chính. Đóng
            // cửa sổ tuỳ chọn không được làm thoát chương trình.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => SaveOnExit();
        }

        AppPaths.EnsureProfile();

        _settings = AppSettings.Load(AppPaths.SettingsFile);
        _macros = MacroStore.Load(AppPaths.MacrosFile, AppPaths.BundledMacros);
        _usage = UsageMemory.Load(AppPaths.UsageFile);
        _dict = LoadDictionary(_settings);
        _engine = new CandidateEngine(_dict, _macros, _usage, _settings);

        _ime = new ImeController(_engine, _settings,
                                 new MacKeyboardHook(), new MacTextOutput(), new MacClipboard());
        _popup = new CandidateWindow(_settings);

        _appModes = AppModeStore.Load(AppPaths.AppConfigFile, AppPaths.AppConfigFallback);
        _ime.AttachAppModes(_appModes);

        _appWatcher = new MacActiveAppWatcher();
        _appWatcher.ActiveAppChanged += (_, app) =>
        {
            _ime.NotifyActiveApp(app.Key, app.Name);
            RefreshTray();
            _options?.OnActiveAppChanged();
        };

        // Cập nhật khung gợi ý được hoãn lại một nhịp thay vì làm ngay: sự kiện
        // này phát ra từ bên trong hàm xử lý của CGEventTap, mà macOS tự tắt tap
        // nếu hàm đó chạy lâu - dựng lại cả cửa sổ ngay tại chỗ là đủ lâu.
        _ime.StateChanged += (_, st) => Dispatcher.UIThread.Post(() => _popup.UpdateState(st));
        _ime.ModeChanged += (_, _) => RefreshTray();
        _popup.CandidateClicked += (_, index) => _ime.CommitByIndex(index);

        // Cùng lý do, nhưng gắt hơn: hai việc này còn phải chờ clipboard vài
        // trăm mili giây, làm trong hàm xử lý tap là chắc chắn bị tắt.
        _ime.AsyncActionRequested += (_, action) => Dispatcher.UIThread.Post(() =>
        {
            switch (action)
            {
                case ImeAsyncAction.ClipboardLookup: _ime.RunClipboardLookup(); break;
                case ImeAsyncAction.ConvertSelection: _ime.RunSelectionConversion(); break;
                case ImeAsyncAction.HanToVietPopup: _ime.RunHanToVietLookup(); break;
            }
        });

        _ime.HanToVietReady += (_, result) => ShowHanVietPopup(result);

        BuildTray();
        StartIme();

        // Chạy cả khi chế độ theo từng ứng dụng đang tắt: danh sách trong
        // config.json vẫn được ghi tiếp, nên lúc bật lên là đã có sẵn nhật ký.
        _appWatcher.Start();

        RefreshTray();

        // Ghi cấu hình ngay lần chạy đầu để lần sau không bị coi là mới cài.
        _settings.Save();

        base.OnFrameworkInitializationCompleted();
    }

    // ------------------------------------------------------------ từ điển

    private static HanNomDictionary LoadDictionary(AppSettings s)
    {
        string main = AppPaths.ResolveMainDict(s.MainDictionaryPath);
        string user = AppPaths.ResolveUserDict(s.UserDictionaryPath);
        string? supplement = s.EnableSupplementDictionary ? AppPaths.ResolveSupplement() : null;
        return HanNomDictionary.Load(main, user, supplement);
    }

    /// <summary>Nạp lại toàn bộ dữ liệu sau khi người dùng đổi từ điển hoặc gõ tắt.</summary>
    public void ReloadDictionary()
    {
        try
        {
            _dict = LoadDictionary(_settings);
            _macros = MacroStore.Load(AppPaths.MacrosFile, AppPaths.BundledMacros);
            _engine = new CandidateEngine(_dict, _macros, _usage, _settings);
            _ime.SwapEngine(_engine, _settings);
            _options?.OnDictionaryReloaded();
        }
        catch (Exception ex) when (ex is IOException or FileNotFoundException)
        {
            _ = Dialogs.ShowMessage(_options, "KeyGo Hán Việt",
                $"Không nạp được từ điển:\n{ex.Message}");
        }
    }

    /// <summary>Áp dụng tuỳ chọn mới từ cửa sổ cấu hình.</summary>
    public void ApplySettings(AppSettings updated, bool dictionaryChanged)
    {
        _settings.CopyFrom(updated);
        _settings.Save();

        _popup.ApplyTheme();
        MacStartupRegistration.Apply(_settings.RunAtStartup);

        if (dictionaryChanged) ReloadDictionary();
        else
        {
            _engine = new CandidateEngine(_dict, _macros, _usage, _settings);
            _ime.SwapEngine(_engine, _settings);
        }

        SetEnabled(_settings.Enabled);

        // Chế độ chọn trong cửa sổ tuỳ chọn cũng là một lần người dùng đổi chế
        // độ, nên phải ghi cho ứng dụng đang ở trước như khi bấm phím chuyển.
        SyncActiveAppMode(record: true);

        RefreshTray();
    }

    /// <summary>
    /// Đồng bộ chế độ hiện tại với bảng nhớ theo ứng dụng.
    ///
    /// <paramref name="record"/> = true thì ghi chế độ đang bật <b>vào</b> ứng
    /// dụng đang ở trước; false thì làm ngược lại - đọc chế độ đã nhớ của ứng
    /// dụng đó ra và áp vào bộ gõ (dùng sau khi người dùng sửa danh sách bằng tay).
    /// </summary>
    public void SyncActiveAppMode(bool record = false)
    {
        if (!_settings.PerAppMode) return;

        string key = _ime.ActiveAppKey;
        if (key.Length == 0) return;

        if (record)
        {
            if (_appModes.SetMode(key, _ime.ActiveAppName, _settings.HanNomMode)) _appModes.Save();
            return;
        }

        bool want = _appModes.GetMode(key);
        if (want == _settings.HanNomMode) return;

        if (want && !_settings.Enabled) SetEnabled(true);
        _ime.HanNomMode = want;
        RefreshTray();
    }

    public AppSettings Settings => _settings;
    public AppModeStore AppModes => _appModes;
    public HanNomDictionary Dict => _dict;
    public CandidateEngine Engine => _engine;
    public MacroStore Macros => _macros;
    public UsageMemory Usage => _usage;
    public ImeController Ime => _ime;

    // ------------------------------------------- cửa sổ xem nhanh âm đọc

    /// <summary>
    /// Hiện cửa sổ nhỏ chứa âm đọc Quốc ngữ của đoạn vừa bôi đen.
    ///
    /// Cửa sổ này nhận tiêu điểm nên phải tạm ngưng bộ gõ trong lúc nó mở: tap
    /// bàn phím bắt phím của cả hệ thống, không ngưng thì gõ vào chính ô kết quả
    /// lại làm bật khung gợi ý.
    /// </summary>
    private void ShowHanVietPopup(HanVietResult result)
    {
        CloseHanVietPopup();

        var popup = new HanVietPopupWindow(_settings, result);
        _hanVietPopup = popup;

        _ime.Suspend();
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_hanVietPopup, popup)) _hanVietPopup = null;
            _ime.Resume();
        };

        popup.ShowNearCaret();
    }

    private void CloseHanVietPopup()
    {
        var popup = _hanVietPopup;
        _hanVietPopup = null;
        popup?.Close();
    }

    // ------------------------------------------------- thanh trình đơn

    private void BuildTray()
    {
        _miMode = new NativeMenuItem("Chế độ Hán Nôm") { ToggleType = NativeMenuItemToggleType.CheckBox };
        _miMode.Click += (_, _) => ToggleMode();

        _miEnabled = new NativeMenuItem("Bật bộ gõ") { ToggleType = NativeMenuItemToggleType.CheckBox };
        _miEnabled.Click += (_, _) => SetEnabled(!_settings.Enabled);

        var miOptions = new NativeMenuItem("Tuỳ chọn…");
        miOptions.Click += (_, _) => ShowOptions();

        var miReload = new NativeMenuItem("Nạp lại từ điển");
        miReload.Click += (_, _) => ReloadDictionary();

        var miQuit = new NativeMenuItem("Thoát");
        miQuit.Click += (_, _) => ExitApp();

        var menu = new NativeMenu
        {
            Items =
            {
                _miMode,
                _miEnabled,
                new NativeMenuItemSeparator(),
                miOptions,
                miReload,
                new NativeMenuItemSeparator(),
                miQuit,
            },
        };

        _tray = new TrayIcon
        {
            Icon = MacTrayIcons.On,
            Menu = menu,
            IsVisible = true,
        };

        // Trên macOS, nhấp vào biểu tượng thanh trình đơn luôn mở trình đơn -
        // đó là quy ước của hệ và NSStatusItem không cho tách hai hành vi.
        // Bản Windows đổi chế độ bằng cú nhấp trái; ở đây việc đó nằm ở mục đầu
        // trình đơn, còn phím chuyển thì vẫn hoạt động y hệt.
        TrayIcon.SetIcons(this, [_tray]);
    }

    private void RefreshTray()
    {
        bool han = IsHanNomActive;

        _tray.Icon = han ? MacTrayIcons.On : MacTrayIcons.Off;
        _tray.ToolTipText = _settings.Enabled
            ? (han ? "KeyGo Hán Việt — đang gõ Hán Nôm (K)" : "KeyGo Hán Việt — đang gõ Latin (Q)")
            : "KeyGo Hán Việt — đã tắt (Q)";

        _miMode.IsChecked = han;
        _miMode.Header = han
            ? $"Đang gõ Hán Nôm — K   ({ToggleKeyName()})"
            : $"Đang gõ Latin — Q   ({ToggleKeyName()})";
        _miEnabled.IsChecked = _settings.Enabled;

        _options?.OnModeChanged();
    }

    // --------------------------------------------------------- bật / tắt

    private void StartIme()
    {
        try
        {
            _ime.Start();
        }
        catch (InvalidOperationException ex)
        {
            _settings.Enabled = false;
            _ = ShowAccessibilityHelp(ex.Message);
        }
    }

    /// <summary>
    /// Không có quyền Trợ năng thì bộ gõ không đọc được phím nào. Chỉ dẫn cho
    /// người dùng và mở thẳng đúng trang cài đặt cần bật.
    /// </summary>
    private static async Task ShowAccessibilityHelp(string message)
    {
        bool open = await Dialogs.Confirm(null, "KeyGo Hán Việt", message,
                                          confirmText: "Mở Cài đặt Hệ thống", cancelText: "Để sau");
        if (!open) return;

        try
        {
            Process.Start(new ProcessStartInfo("open",
                "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
            { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Không mở được thì thôi, chỉ dẫn trong hộp thoại đã đủ để tự làm.
        }
    }

    private void SetEnabled(bool on)
    {
        _settings.Enabled = on;

        if (on)
        {
            if (!_ime.IsRunning) StartIme();
        }
        else
        {
            // Tắt hẳn thì gỡ luôn tap, không chỉ ẩn khung gợi ý - có vậy
            // chương trình mới thực sự không đụng gì tới bàn phím.
            _ime.Stop();
            _popup.HidePopup();
            CloseHanVietPopup();
        }

        RefreshTray();
    }

    /// <summary>true khi bộ gõ đang thực sự chuyển chữ Hán Nôm (biểu tượng K).</summary>
    public bool IsHanNomActive => _settings.Enabled && _settings.HanNomMode;

    /// <summary>Đổi qua lại giữa K (gõ Hán Nôm) và Q (tắt bộ gõ).</summary>
    public void ToggleMode()
    {
        if (IsHanNomActive)
        {
            _ime.HanNomMode = false;
        }
        else
        {
            // Từ Q về K: nếu trước đó bộ gõ bị tắt hẳn thì phải bật lại.
            if (!_settings.Enabled) SetEnabled(true);
            _ime.HanNomMode = true;
        }
        RefreshTray();
    }

    public string ToggleKeyName() => _settings.ToggleKey switch
    {
        ToggleKeyKind.ShiftTap => "Shift",
        ToggleKeyKind.ControlShift => "Ctrl + Shift",
        ToggleKeyKind.AltZ => "⌥ + Z",
        ToggleKeyKind.ControlSpace => "Ctrl + Space",
        ToggleKeyKind.Custom => HotkeySpec.Describe(HotkeySpec.Parse(_settings.CustomToggleKey)),
        _ => "(không dùng)",
    };

    // ------------------------------------------------------- cửa sổ tuỳ chọn

    public void ShowOptions()
    {
        if (_options is null)
        {
            _options = new OptionsWindow(this);
            _options.Closed += (_, _) => _options = null;
        }

        _options.Show();
        _options.WindowState = WindowState.Normal;
        _options.Activate();
    }

    private void ExitApp()
    {
        SaveOnExit();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void SaveOnExit()
    {
        _usage?.Save();
        _settings?.Save();
        _appModes?.Save();
        _appWatcher?.Stop();
        CloseHanVietPopup();
        if (_tray is not null) _tray.IsVisible = false;
        _ime?.Stop();
    }
}
