internal static partial class SmokeTests
{
static void NightBacklightTimeSupportsCrossMidnight()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            Type? formType = typeof(LibreHardwareMonitor.UI.TreeModel).Assembly.GetType(
                "LibreHardwareMonitor.UI.NightBacklightSettingsForm");
            True(formType is not null, "未找到夜间背光设置窗体");

            var existing = new LibreHardwareMonitor.Solis.DeviceControl.DeviceDisplaySettings(
                100,
                true,
                23 * 60,
                7 * 60,
                8 * 60);
            using var form = (Form)Activator.CreateInstance(formType!, existing)!;
            DateTimePicker startInput = GetPrivateField<DateTimePicker>(formType!, form, "_start");
            DateTimePicker endInput = GetPrivateField<DateTimePicker>(formType!, form, "_end");
            startInput.Value = DateTime.Today.AddHours(23).AddMinutes(59);
            endInput.Value = DateTime.Today.AddHours(7);

            InvokePrivate(formType!, form, "SaveClick", null, EventArgs.Empty);
            Equal(DialogResult.OK, form.DialogResult, "跨午夜时间未能保存");
            Equal(1439, GetPublicProperty<int>(formType!, form, "NightStartMinute"),
                "夜间背光开始时间错误");
            Equal(420, GetPublicProperty<int>(formType!, form, "NightEndMinute"),
                "夜间背光结束时间错误");
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(uiThread.Join(TimeSpan.FromSeconds(10)), "夜间背光跨午夜测试超时");
    if (failure != null)
        throw new InvalidOperationException("夜间背光跨午夜测试失败", failure);
}

static void NightBacklightUsesNativeTimeSelectors()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            Type? windowType = typeof(LibreHardwareMonitor.UI.TreeModel).Assembly.GetType(
                "LibreHardwareMonitor.UI.WpfViews.SolisNightBacklightSettingsWindow");
            True(windowType is not null, "未找到原生 WPF 夜间背光设置窗口");

            var existing = new LibreHardwareMonitor.Solis.DeviceControl.DeviceDisplaySettings(
                100,
                true,
                (23 * 60) + 59,
                7 * 60,
                8 * 60);
            var window = (System.Windows.Window)Activator.CreateInstance(
                windowType!,
                existing)!;
            try
            {
                True(
                    window.Resources.MergedDictionaries.Count >= 2,
                    "夜间背光窗口没有加载 WPF UI 主题与控件资源");
                var startHour = (System.Windows.Controls.ComboBox?)
                    window.FindName("StartHourInput");
                var startMinute = (System.Windows.Controls.ComboBox?)
                    window.FindName("StartMinuteInput");
                var endHour = (System.Windows.Controls.ComboBox?)
                    window.FindName("EndHourInput");
                var endMinute = (System.Windows.Controls.ComboBox?)
                    window.FindName("EndMinuteInput");
                True(
                    startHour is not null &&
                    startMinute is not null &&
                    endHour is not null &&
                    endMinute is not null,
                    "夜间背光窗口缺少原生小时或分钟选择器");
                Equal(24, startHour!.Items.Count, "开始小时选项不完整");
                Equal(60, startMinute!.Items.Count, "开始分钟选项不完整");
                Equal("23", startHour.SelectedItem?.ToString(),
                    "开始小时没有载入已有设置");
                Equal("59", startMinute.SelectedItem?.ToString(),
                    "开始分钟没有载入已有设置");
                Equal("07", endHour!.SelectedItem?.ToString(),
                    "结束小时没有载入已有设置");
                Equal("00", endMinute!.SelectedItem?.ToString(),
                    "结束分钟没有载入已有设置");

                startHour.SelectedItem = "23";
                startMinute.SelectedItem = "59";
                endHour.SelectedItem = "07";
                endMinute.SelectedItem = "00";
                bool saved = (bool)windowType!.GetMethod(
                    "ApplySelection",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, null)!;
                True(saved, "跨午夜时间未能通过 WPF 设置窗口保存");
                var settings =
                    (LibreHardwareMonitor.Solis.DeviceControl.DeviceDisplaySettings)
                    windowType.GetProperty("Settings")!.GetValue(window)!;
                Equal(1439, settings.NightStartMinute,
                    "WPF 夜间背光开始时间错误");
                Equal(420, settings.NightEndMinute,
                    "WPF 夜间背光结束时间错误");
            }
            finally
            {
                window.Close();
            }
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(uiThread.Join(TimeSpan.FromSeconds(10)), "夜间背光原生选择器测试超时");
    if (failure != null)
        throw new InvalidOperationException("夜间背光原生选择器测试失败", failure);
}

static void NightBacklightTimeDropdownIsReadable()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            Type? windowType = typeof(LibreHardwareMonitor.UI.TreeModel).Assembly.GetType(
                "LibreHardwareMonitor.UI.WpfViews.SolisNightBacklightSettingsWindow");
            True(windowType is not null, "未找到原生 WPF 夜间背光设置窗口");

            var existing = new LibreHardwareMonitor.Solis.DeviceControl.DeviceDisplaySettings(
                100,
                true,
                (23 * 60) + 59,
                7 * 60,
                8 * 60);
            var window = (System.Windows.Window)Activator.CreateInstance(
                windowType!,
                existing)!;
            try
            {
                var startHour = (System.Windows.Controls.ComboBox?)
                    window.FindName("StartHourInput");
                True(startHour is not null, "未找到夜间背光开始小时选择器");

                var popupBackground = startHour!.FindResource(
                    System.Windows.SystemColors.WindowBrushKey)
                    as System.Windows.Media.SolidColorBrush;
                var foreground =
                    startHour.Foreground as System.Windows.Media.SolidColorBrush;
                True(
                    popupBackground is not null && foreground is not null,
                    "夜间背光时间选择器缺少可验证的前景或下拉背景");
                True(
                    ContrastRatio(
                        foreground!.Color,
                        popupBackground!.Color) >= 4.5,
                    "夜间背光时间下拉项的文字与背景对比度不足");
            }
            finally
            {
                window.Close();
            }
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(uiThread.Join(TimeSpan.FromSeconds(10)), "夜间背光下拉配色测试超时");
    if (failure != null)
        throw new InvalidOperationException("夜间背光下拉配色测试失败", failure);
}

static void NightBacklightCollapsedTimeSelectionIsReadable()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            Type? windowType = typeof(LibreHardwareMonitor.UI.TreeModel).Assembly.GetType(
                "LibreHardwareMonitor.UI.WpfViews.SolisNightBacklightSettingsWindow");
            True(windowType is not null, "未找到原生 WPF 夜间背光设置窗口");

            var existing = new LibreHardwareMonitor.Solis.DeviceControl.DeviceDisplaySettings(
                100,
                true,
                (21 * 60) + 53,
                7 * 60,
                8 * 60);
            var window = (System.Windows.Window)Activator.CreateInstance(
                windowType!,
                existing)!;
            try
            {
                var startHour = (System.Windows.Controls.ComboBox?)
                    window.FindName("StartHourInput");
                True(startHour is not null, "未找到夜间背光开始小时选择器");

                window.Measure(new System.Windows.Size(560, 400));
                window.Arrange(new System.Windows.Rect(0, 0, 560, 400));
                window.UpdateLayout();
                True(startHour!.ApplyTemplate(), "夜间背光时间选择器模板未能加载");

                const int width = 120;
                const int height = 40;
                startHour.Measure(new System.Windows.Size(width, height));
                startHour.Arrange(new System.Windows.Rect(0, 0, width, height));
                startHour.UpdateLayout();

                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    width,
                    height,
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(startHour);
                var pixels = new byte[width * height * 4];
                bitmap.CopyPixels(
                    new System.Windows.Int32Rect(0, 0, width, height),
                    pixels,
                    width * 4,
                    0);

                int sampleOffset = ((height / 2) * width + 8) * 4;
                var visibleBackground = System.Windows.Media.Color.FromRgb(
                    pixels[sampleOffset + 2],
                    pixels[sampleOffset + 1],
                    pixels[sampleOffset]);
                var foreground =
                    startHour.Foreground as System.Windows.Media.SolidColorBrush;
                True(foreground is not null, "夜间背光时间选择器缺少可验证的前景色");
                True(
                    ContrastRatio(
                        foreground!.Color,
                        visibleBackground) >= 4.5,
                    $"夜间背光时间收起状态对比度不足，实际背景 #{visibleBackground.R:X2}{visibleBackground.G:X2}{visibleBackground.B:X2}");
            }
            finally
            {
                window.Close();
            }
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(uiThread.Join(TimeSpan.FromSeconds(10)), "夜间背光收起状态配色测试超时");
    if (failure != null)
        throw new InvalidOperationException("夜间背光收起状态配色测试失败", failure);
}

static double ContrastRatio(
    System.Windows.Media.Color foreground,
    System.Windows.Media.Color background)
{
    static double RelativeLuminance(System.Windows.Media.Color color)
    {
        static double Linearize(byte component)
        {
            double value = component / 255d;
            return value <= 0.03928
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return
            (0.2126 * Linearize(color.R)) +
            (0.7152 * Linearize(color.G)) +
            (0.0722 * Linearize(color.B));
    }

    double foregroundLuminance = RelativeLuminance(foreground);
    double backgroundLuminance = RelativeLuminance(background);
    double lighter = Math.Max(foregroundLuminance, backgroundLuminance);
    double darker = Math.Min(foregroundLuminance, backgroundLuminance);
    return (lighter + 0.05) / (darker + 0.05);
}

static void WpfControlCenterPageSwitchingKeepsOnePageVisible()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            Type? viewType = typeof(LibreHardwareMonitor.UI.TreeModel).Assembly.GetType(
                "LibreHardwareMonitor.UI.WpfViews.SolisControlCenterView");
            True(viewType is not null, "未找到 WPF 控制中心");

            object view = Activator.CreateInstance(viewType!)!;
            System.Reflection.MethodInfo? showPage = viewType!.GetMethod("ShowPage");
            True(showPage is not null, "未找到 WPF 页面切换入口");

            string[] pages = ["Device", "Service", "Startup", "Firmware"];
            Dictionary<string, object> pageElements =
                pages.ToDictionary(
                    page => page,
                    page => GetPrivateField<object>(
                        viewType,
                        view,
                        $"{page}Page"));

            Type? sizeType = Type.GetType("System.Windows.Size, WindowsBase");
            Type? rectType = Type.GetType("System.Windows.Rect, WindowsBase");
            True(sizeType is not null && rectType is not null,
                "未找到 WPF 布局几何类型");
            object available = Activator.CreateInstance(sizeType!, [1100d, 800d])!;
            object bounds = Activator.CreateInstance(
                rectType!,
                [0d, 0d, 1100d, 800d])!;
            System.Reflection.MethodInfo? measure =
                viewType.GetMethod("Measure", [sizeType!]);
            System.Reflection.MethodInfo? arrange =
                viewType.GetMethod("Arrange", [rectType!]);
            System.Reflection.MethodInfo? updateLayout =
                viewType.GetMethod("UpdateLayout", Type.EmptyTypes);
            True(measure is not null && arrange is not null && updateLayout is not null,
                "未找到 WPF 布局入口");
            for (int cycle = 0; cycle < 20; cycle++)
            {
                foreach (string page in pages)
                {
                    showPage!.Invoke(view, [page]);
                    measure!.Invoke(view, [available]);
                    arrange!.Invoke(view, [bounds]);
                    updateLayout!.Invoke(view, null);

                    foreach ((string candidate, object element) in pageElements)
                    {
                        string expected = candidate == page
                            ? "Visible"
                            : "Collapsed";
                        string actual = element.GetType()
                            .GetProperty("Visibility")!
                            .GetValue(element)!
                            .ToString()!;
                        Equal(expected, actual,
                            $"切换到 {page} 后 {candidate} 页面可见性错误");
                    }

                    double actualWidth = (double)viewType
                        .GetProperty("ActualWidth")!
                        .GetValue(view)!;
                    double actualHeight = (double)viewType
                        .GetProperty("ActualHeight")!
                        .GetValue(view)!;
                    Equal(1100d, actualWidth, "WPF 控制中心布局宽度错误");
                    Equal(800d, actualHeight, "WPF 控制中心布局高度错误");
                }
            }

            object largeAvailable = Activator.CreateInstance(
                sizeType!,
                [1426d, 993d])!;
            object largeBounds = Activator.CreateInstance(
                rectType!,
                [0d, 0d, 1426d, 993d])!;
            showPage!.Invoke(view, ["Device"]);
            measure!.Invoke(view, [largeAvailable]);
            arrange!.Invoke(view, [largeBounds]);
            updateLayout!.Invoke(view, null);

            object deviceDetailsGrid = GetPrivateField<object>(
                viewType,
                view,
                "DeviceDetailsGrid");
            double deviceDetailsHeight = (double)deviceDetailsGrid
                .GetType()
                .GetProperty("ActualHeight")!
                .GetValue(deviceDetailsGrid)!;
            True(deviceDetailsHeight is >= 499.5 and <= 500.5,
                $"大视口下设备详情卡片应保持 500 DIP，实际高度 {deviceDetailsHeight:0.0}");
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(uiThread.Join(TimeSpan.FromSeconds(15)), "WPF 控制中心连续切页测试超时");
    if (failure != null)
        throw new InvalidOperationException("WPF 控制中心连续切页测试失败", failure);
}

static void NativeWpfMainWindowHostsControlCenterView()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            System.Reflection.Assembly assembly =
                typeof(LibreHardwareMonitor.UI.TreeModel).Assembly;
            Type? windowType = assembly.GetType(
                "LibreHardwareMonitor.UI.WpfViews.SolisMainWindow");
            True(windowType is not null, "未找到原生 WPF 主窗口");

            Type? wpfWindowType = Type.GetType(
                "System.Windows.Window, PresentationFramework");
            True(wpfWindowType is not null, "未找到 WPF Window 类型");
            True(wpfWindowType!.IsAssignableFrom(windowType!),
                "SolisMainWindow 必须继承 WPF Window");
            True(!typeof(Form).IsAssignableFrom(windowType!),
                "SolisMainWindow 不得继承 WinForms Form");

            object window = Activator.CreateInstance(windowType!)!;
            try
            {
                object? content = windowType!.GetProperty("Content")!
                    .GetValue(window);
                True(content is not null, "WPF 主窗口没有内容");
                Equal(
                    "LibreHardwareMonitor.UI.WpfViews.SolisControlCenterView",
                    content!.GetType().FullName,
                    "WPF 主窗口没有直接承载控制中心视图");
                True(!content.GetType().FullName!.Contains(
                        "ElementHost",
                        StringComparison.Ordinal),
                    "WPF 主窗口不得通过 ElementHost 承载内容");
            }
            finally
            {
                windowType!.GetMethod("Close", Type.EmptyTypes)!
                    .Invoke(window, null);
            }
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(uiThread.Join(TimeSpan.FromSeconds(10)), "原生 WPF 主窗口测试超时");
    if (failure != null)
        throw new InvalidOperationException("原生 WPF 主窗口测试失败", failure);
}

static void NativeWpfMainWindowAcceptsInjectedControlCenterView()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            System.Reflection.Assembly assembly =
                typeof(LibreHardwareMonitor.UI.TreeModel).Assembly;
            Type? windowType = assembly.GetType(
                "LibreHardwareMonitor.UI.WpfViews.SolisMainWindow");
            Type? viewType = assembly.GetType(
                "LibreHardwareMonitor.UI.WpfViews.SolisControlCenterView");
            True(windowType is not null && viewType is not null,
                "未找到原生 WPF 主窗口或控制中心视图");

            object view = Activator.CreateInstance(viewType!)!;
            System.Reflection.ConstructorInfo? constructor =
                windowType!.GetConstructor([viewType!]);
            True(constructor is not null,
                "WPF 主窗口必须接受已创建的控制中心视图");

            object window = constructor!.Invoke([view]);
            try
            {
                object? content = windowType.GetProperty("Content")!
                    .GetValue(window);
                True(ReferenceEquals(view, content),
                    "WPF 主窗口没有承载传入的同一控制中心视图实例");
            }
            finally
            {
                windowType.GetMethod("Close", Type.EmptyTypes)!
                    .Invoke(window, null);
            }
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(uiThread.Join(TimeSpan.FromSeconds(10)),
        "原生 WPF 主窗口注入测试超时");
    if (failure != null)
        throw new InvalidOperationException(
            "原生 WPF 主窗口注入测试失败",
            failure);
}

static void NativeWpfDesktopHostOwnsBackendWindowAndTaskbar()
{
    System.Reflection.Assembly assembly =
        typeof(LibreHardwareMonitor.UI.TreeModel).Assembly;
    Type? hostType = assembly.GetType(
        "LibreHardwareMonitor.UI.WpfViews.SolisWpfDesktopHost");
    True(hostType is not null, "未找到原生 WPF 桌面宿主");
    True(typeof(IDisposable).IsAssignableFrom(hostType!),
        "原生 WPF 桌面宿主必须可释放");

    Type[] fieldTypes = hostType!
        .GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public)
        .Select(field => field.FieldType)
        .ToArray();
    string[] requiredTypes =
    [
        "LibreHardwareMonitor.Solis.Desktop.SolisDesktopBackend",
        "LibreHardwareMonitor.UI.WpfViews.SolisControlCenterView",
        "LibreHardwareMonitor.UI.WpfViews.SolisMainWindow",
        "LibreHardwareMonitor.UI.WpfViews.SolisTaskbarHost",
        "LibreHardwareMonitor.UI.WpfViews.SolisDesktopPresenter",
        "System.Windows.Threading.DispatcherTimer"
    ];
    foreach (string requiredType in requiredTypes)
    {
        Equal(1, fieldTypes.Count(type => type.FullName == requiredType),
            $"原生 WPF 桌面宿主必须只拥有一个 {requiredType}");
    }

    True(!fieldTypes.Any(type =>
            type.FullName?.StartsWith(
                "System.Windows.Forms.",
                StringComparison.Ordinal) == true ||
            type.FullName == "System.Windows.Forms.Integration.ElementHost" ||
            type.FullName == "LibreHardwareMonitor.UI.MainForm"),
        "原生 WPF 桌面宿主不得持有 WinForms 窗口或桥接控件");

    foreach (string methodName in new[] { "Start", "ShowWindow", "RequestExit" })
    {
        True(hostType.GetMethod(methodName) is not null,
            $"原生 WPF 桌面宿主缺少 {methodName} 生命周期入口");
    }
}

static void DeveloperModeSwitchesHostInCurrentProcess()
{
    System.Reflection.Assembly assembly =
        typeof(LibreHardwareMonitor.UI.TreeModel).Assembly;
    Type? applicationType = assembly.GetType(
        "LibreHardwareMonitor.UI.WpfViews.SolisApplication");
    Type? desktopHostType = assembly.GetType(
        "LibreHardwareMonitor.UI.WpfViews.SolisWpfDesktopHost");
    Type? mainFormType = assembly.GetType(
        "LibreHardwareMonitor.UI.MainForm");

    True(applicationType is not null, "未找到 WPF 应用入口");
    True(desktopHostType is not null, "未找到 WPF 桌面宿主");
    True(mainFormType is not null, "未找到旧版开发者窗口");
    True(
        applicationType!.GetField(
            "_launchLegacyUiOnExit",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic) is null,
        "开发者模式仍通过退出并重启进程切换");
    True(
        applicationType.GetFields(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)
            .Any(field => field.FieldType == mainFormType),
        "WPF 应用没有在当前进程持有开发者窗口");
    True(
        desktopHostType!.GetMethod(
            "CloseForHostSwitch",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public,
            null,
            new[] { typeof(bool) },
            null) is not null,
        "WPF 桌面宿主缺少保留后台运行时的切换入口");
    True(
        desktopHostType.GetProperty(
            "Runtime",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public) is not null,
        "WPF 桌面宿主没有公开可转交的后台运行时");
    True(
        mainFormType!.GetConstructor(
            new[]
            {
                typeof(string[]),
                typeof(LibreHardwareMonitor.Solis.SolisRuntime)
            }) is not null,
        "开发者窗口无法复用 WPF 后台运行时");
    True(
        mainFormType.GetMethod(
            "ShowDeveloperModeFromExternalLaunch",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic) is not null,
        "旧版窗口缺少直接进入开发者模式的入口");
}

static void NativeWpfServiceActionsCopyDiagnosticsAndOpenCodex()
{
    System.Reflection.Assembly assembly =
        typeof(LibreHardwareMonitor.UI.TreeModel).Assembly;
    Type? actionsType = assembly.GetType(
        "LibreHardwareMonitor.UI.WpfViews.SolisDesktopServiceActions");
    True(actionsType is not null, "未找到原生 WPF 服务操作");

    string? copiedText = null;
    string? openedPath = null;
    int editWeatherCount = 0;
    int deviceWizardCount = 0;
    int firmwareUpdateCount = 0;
    string codexRoot = Path.Combine(
        Path.GetTempPath(),
        "SolisMonitor-Codex");
    DateTimeOffset now = new(
        2026,
        7,
        29,
        12,
        34,
        56,
        TimeSpan.FromHours(8));
    object? actions = Activator.CreateInstance(
        actionsType!,
        new Func<LibreHardwareMonitor.Solis.Diagnostics.SolisDiagnosticsSnapshot>(
            () => LibreHardwareMonitor.Solis.Diagnostics
                .SolisDiagnosticsSnapshot.Initial),
        new Func<string>(() => "0.9.6-test"),
        new Func<DateTimeOffset>(() => now),
        new Func<string>(() => codexRoot),
        new Action<string>(text => copiedText = text),
        new Action<string>(path => openedPath = path),
        new Action(() => editWeatherCount++),
        new Action(() => deviceWizardCount++),
        new Action(() => firmwareUpdateCount++));
    True(actions is not null, "无法创建原生 WPF 服务操作");

    actionsType!.GetMethod("CopyDiagnostics")!.Invoke(actions, null);
    actionsType.GetMethod("OpenCodexSessions")!.Invoke(actions, null);
    actionsType.GetMethod("EditWeather")!.Invoke(actions, null);
    actionsType.GetMethod("ShowDeviceWizard")!.Invoke(actions, null);
    actionsType.GetMethod("ShowFirmwareUpdate")!.Invoke(actions, null);

    True(
        copiedText?.Contains(
            "Solis Monitor 诊断信息",
            StringComparison.Ordinal) == true,
        "复制操作没有生成诊断信息");
    True(
        copiedText?.Contains(
            "程序版本：0.9.6-test",
            StringComparison.Ordinal) == true,
        "诊断信息没有使用当前程序版本");
    True(
        copiedText?.Contains(
            "API Key、Wi-Fi 密码和完整设备令牌未包含",
            StringComparison.Ordinal) == true,
        "诊断信息没有保留敏感信息排除声明");
    Equal(codexRoot, openedPath, "打开操作没有使用 Codex 采集目录");
    Equal(1, editWeatherCount, "天气编辑操作没有调用原生窗口入口");
    Equal(1, deviceWizardCount, "设备向导操作没有调用原生窗口入口");
    Equal(1, firmwareUpdateCount, "固件更新操作没有调用原生窗口入口");
}

static void NativeWpfDeviceSetupWizardUsesNativeWindows()
{
    System.Reflection.Assembly assembly =
        typeof(LibreHardwareMonitor.UI.TreeModel).Assembly;
    foreach (string typeName in new[]
             {
                 "LibreHardwareMonitor.UI.WpfViews.SolisDeviceSetupWizardWindow",
                 "LibreHardwareMonitor.UI.WpfViews.SolisPairingCodeWindow"
             })
    {
        Type? windowType = assembly.GetType(typeName);
        True(windowType is not null, $"未找到原生 WPF 窗口 {typeName}");
        True(
            typeof(System.Windows.Window).IsAssignableFrom(windowType!),
            $"{typeName} 必须使用原生 WPF Window");
        True(
            !typeof(Form).IsAssignableFrom(windowType!),
            $"{typeName} 不得回退到 WinForms 窗口");
    }
}

static void NativeWpfFirmwareConfirmationUsesNativeWindow()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            System.Reflection.Assembly assembly =
                typeof(LibreHardwareMonitor.UI.TreeModel).Assembly;
            Type? windowType = assembly.GetType(
                "LibreHardwareMonitor.UI.WpfViews.SolisFirmwareConfirmationWindow");
            True(windowType is not null, "未找到原生 WPF 固件确认窗口");
            True(
                typeof(System.Windows.Window).IsAssignableFrom(windowType!),
                "固件确认窗口必须使用原生 WPF Window");
            True(
                !typeof(Form).IsAssignableFrom(windowType!),
                "固件确认窗口不得回退到 WinForms 窗口");

            var image = new LibreHardwareMonitor.Solis.Firmware.FirmwareImageInfo(
                "solis_monitor",
                "0.1.6",
                0x0009,
                3_523_215,
                "72FB831EF5D54636AABBCCDDEEFF00112233445566778899AABBCCDDEEFF0011");
            var window = (System.Windows.Window)Activator.CreateInstance(
                windowType!,
                "solis_monitor-0.1.6.bin",
                image)!;
            var details = (System.Windows.Controls.TextBlock?)
                window.FindName("FirmwareDetailsText");

            True(details is not null, "固件确认窗口缺少可见的固件详情");
            True(
                details!.Text.Contains(
                    "solis_monitor-0.1.6.bin",
                    StringComparison.Ordinal),
                "固件确认窗口没有显示文件名");
            True(
                details.Text.Contains("版本：0.1.6", StringComparison.Ordinal),
                "固件确认窗口没有显示版本");
            True(
                details.Text.Contains("3.36 MB", StringComparison.Ordinal),
                "固件确认窗口没有显示格式化后的大小");
            True(
                details.Text.Contains(
                    "SHA-256：\n" +
                    "72FB831EF5D54636AABBCCDDEEFF0011\n" +
                    "2233445566778899AABBCCDDEEFF0011",
                    StringComparison.Ordinal),
                "固件确认窗口没有分行完整显示 SHA-256");
            window.Close();
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(
        uiThread.Join(TimeSpan.FromSeconds(10)),
        "原生 WPF 固件确认窗口测试超时");
    if (failure != null)
        throw new InvalidOperationException(
            "原生 WPF 固件确认窗口测试失败",
            failure);
}

static void NativeWpfDeviceSetupWizardUsesRuntimeActions()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            var candidate =
                new LibreHardwareMonitor.Solis.DeviceControl.DiscoveredDevice(
                    "Solis_Monitor_TEST",
                    "0.1.5",
                    "192.168.0.40",
                    -42,
                    false,
                    true);
            int scanCount = 0;
            int maintenanceCount = 0;
            int pairingCount = 0;
            string? pairingCode = null;
            var window =
                new LibreHardwareMonitor.UI.WpfViews
                    .SolisDeviceSetupWizardWindow(
                        () => new LibreHardwareMonitor.Solis.DeviceControl
                            .DeviceDiscoveryState(null, false, null),
                        () => new[] { candidate },
                        () => scanCount++,
                        (device, code, _) =>
                        {
                            Equal(candidate, device, "向导没有传递选中的副屏");
                            pairingCount++;
                            pairingCode = code;
                            return Task.FromResult(
                                new LibreHardwareMonitor.Solis.DeviceControl
                                    .DevicePairingResult(true));
                        },
                        () => maintenanceCount++);

            InvokePrivate(window.GetType(), window, "BeginDiscovery");
            InvokePrivate(window.GetType(), window, "BeginProvisioning");
            var pairingTask = (Task<
                LibreHardwareMonitor.Solis.DeviceControl.DevicePairingResult>)
                window.GetType()
                    .GetMethod(
                        "PairDeviceAsync",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, [candidate, "123456"])!;
            LibreHardwareMonitor.Solis.DeviceControl.DevicePairingResult result =
                pairingTask.GetAwaiter().GetResult();

            Equal(1, scanCount, "进入发现页没有触发设备扫描");
            Equal(1, maintenanceCount, "进入配网页没有启动维护窗口");
            Equal(1, pairingCount, "原生设备向导没有调用运行时配对入口");
            Equal("123456", pairingCode, "原生设备向导没有传递配对码");
            True(result.Success, "原生设备向导没有返回配对结果");
            InvokePrivate(
                window.GetType(),
                window,
                "CompleteSuccessfulPairing",
                candidate);
            Equal(1, scanCount, "首次配对成功后不应再次触发扫描覆盖配对状态");
            window.Close();
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(
        uiThread.Join(TimeSpan.FromSeconds(10)),
        "原生 WPF 设备向导行为测试超时");
    if (failure != null)
        throw new InvalidOperationException(
            "原生 WPF 设备向导行为测试失败",
            failure);
}

static void NativeWpfWeatherEditorValidatesAndPreservesKey()
{
    var existing =
        new LibreHardwareMonitor.Solis.Weather.QWeatherSettings(
            true,
            "weather.example.com",
            "existing-key",
            string.Empty,
            null,
            121.51,
            38.84);
    LibreHardwareMonitor.Solis.Weather.QWeatherSettings? testedSettings = null;
    LibreHardwareMonitor.Solis.Weather.QWeatherSettings? savedSettings = null;
    LibreHardwareMonitor.Solis.Weather.WeatherMetricsReading? savedReading = null;
    var reading =
        new LibreHardwareMonitor.Solis.Weather.WeatherMetricsReading(
            true,
            "辽宁·大连",
            "晴",
            20,
            28,
            null);
    var editor =
        new LibreHardwareMonitor.UI.WpfViews.SolisWeatherSettingsEditor(
            existing,
            settings =>
            {
                testedSettings = settings;
                return reading;
            },
            (settings, tested) =>
            {
                savedSettings = settings;
                savedReading = tested;
            });

    True(
        editor.TryCreateSettings(
            "weather.example.com",
            string.Empty,
            "121.51，38.84",
            out LibreHardwareMonitor.Solis.Weather.QWeatherSettings? settings,
            out string error),
        $"合法天气配置未通过校验：{error}");
    Equal("existing-key", settings!.ApiKey, "空 API Key 没有保留已有密钥");
    Equal(121.51, settings.Longitude, "经度解析错误");
    Equal(38.84, settings.Latitude, "纬度解析错误");
    Equal("121.51,38.84", editor.FormatCoordinates(),
        "已有经纬度没有格式化为单行输入");

    LibreHardwareMonitor.Solis.Weather.WeatherMetricsReading tested =
        editor.Test(settings);
    editor.Save(settings, tested);
    Equal(settings, testedSettings, "天气测试没有使用已校验配置");
    Equal(settings, savedSettings, "天气保存没有使用测试通过的配置");
    Equal(reading, savedReading, "天气保存没有携带测试结果");

    True(
        !editor.TryCreateSettings(
            "https://weather.example.com",
            "new-key",
            "121.51,38.84",
            out _,
            out _),
        "带协议的 API Host 不应通过校验");
}

static void NativeWpfWeatherSettingsUsesNativeWindow()
{
    System.Reflection.Assembly assembly =
        typeof(LibreHardwareMonitor.UI.TreeModel).Assembly;
    Type? windowType = assembly.GetType(
        "LibreHardwareMonitor.UI.WpfViews.SolisWeatherSettingsWindow");
    True(windowType is not null, "未找到原生 WPF 天气设置窗口");
    True(
        typeof(System.Windows.Window).IsAssignableFrom(windowType!),
        "天气设置必须使用原生 WPF Window");
    True(
        !typeof(Form).IsAssignableFrom(windowType!),
        "天气设置不得回退到 WinForms 窗口");
}

static void NativeWpfPresenterWiresServiceActions()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"SolisMonitor.WpfActions-{Guid.NewGuid():N}");
        string codexRoot = Path.Combine(directory, "codex");
        Directory.CreateDirectory(codexRoot);

        LibreHardwareMonitor.Solis.SolisRuntime? runtime = null;
        IDisposable? presenter = null;
        try
        {
            var listener = new System.Net.Sockets.TcpListener(
                IPAddress.Loopback,
                0);
            listener.Start();
            int port =
                ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            runtime = new LibreHardwareMonitor.Solis.SolisRuntime(
                string.Empty,
                directory,
                codexRoot,
                "127.0.0.1",
                port);
            var view =
                new LibreHardwareMonitor.UI.WpfViews.SolisControlCenterView();
            var startup =
                new LibreHardwareMonitor.Solis.Desktop
                    .DesktopStartupSettingsController(
                        () => false,
                        _ => { },
                        () => false,
                        _ => { });
            string? copiedText = null;
            string? openedPath = null;
            int editWeatherCount = 0;
            int deviceWizardCount = 0;
            int firmwareUpdateCount = 0;
            int developerModeCount = 0;
            var actions =
                new LibreHardwareMonitor.UI.WpfViews
                    .SolisDesktopServiceActions(
                        () => runtime.Diagnostics,
                        () => "0.9.6-test",
                        () => DateTimeOffset.Now,
                        () => runtime.CodexSessionsRoot,
                        text => copiedText = text,
                        path => openedPath = path,
                        () => editWeatherCount++,
                        () => deviceWizardCount++,
                        () => firmwareUpdateCount++,
                        enterDeveloperMode: () => developerModeCount++);

            Type presenterType = typeof(LibreHardwareMonitor.UI.TreeModel)
                .Assembly
                .GetType(
                    "LibreHardwareMonitor.UI.WpfViews.SolisDesktopPresenter")!;
            System.Reflection.ConstructorInfo? constructor =
                presenterType.GetConstructor(
                [
                    typeof(LibreHardwareMonitor.Solis.SolisRuntime),
                    typeof(LibreHardwareMonitor.UI.WpfViews
                        .SolisControlCenterView),
                    typeof(LibreHardwareMonitor.Solis.Desktop
                        .DesktopStartupSettingsController),
                    typeof(LibreHardwareMonitor.UI.WpfViews
                        .SolisDesktopServiceActions)
                ]);
            True(
                constructor is not null,
                "原生 WPF 呈现器没有接受服务操作");
            presenter = (IDisposable)constructor!.Invoke(
                [runtime, view, startup, actions]);
            runtime.SaveWeatherSettings(
                new LibreHardwareMonitor.Solis.Weather.QWeatherSettings(
                    true,
                    "weather.example.com",
                    "test-key",
                    "121.51,38.84",
                    null),
                new LibreHardwareMonitor.Solis.Weather.WeatherMetricsReading(
                    true,
                    "辽宁·大连",
                    "晴",
                    24,
                    31,
                    null));
            runtime.Start();
            presenterType.GetMethod("Refresh")!.Invoke(presenter, null);

            RaiseServiceEvent(
                view.ServiceView,
                "CopyDiagnosticsRequested");
            RaiseServiceEvent(
                view.ServiceView,
                "OpenCodexRequested");
            RaiseServiceEvent(
                view.ServiceView,
                "EditWeatherRequested");
            RaiseControlCenterEvent(
                view,
                "DeviceWizardRequested");
            RaiseControlCenterEvent(
                view,
                "FirmwareSelectRequested");
            RaiseControlCenterEvent(
                view,
                "DeveloperRequested");

            True(
                copiedText?.Contains(
                    "Solis Monitor 诊断信息",
                    StringComparison.Ordinal) == true,
                "服务页复制诊断事件没有接通");
            Equal(
                runtime.CodexSessionsRoot,
                openedPath,
                "服务页采集目录事件没有接通");
            Equal(1, editWeatherCount, "服务页天气编辑事件没有接通");
            Equal(1, deviceWizardCount, "设备页向导事件没有接通");
            Equal(1, firmwareUpdateCount, "固件页选择事件没有接通");
            Equal(1, developerModeCount, "开发者模式入口没有接通");
            Equal(
                "版本 0.9.6-test",
                GetNamedText(view, "VersionText"),
                "原生 WPF 没有显示应用版本");
            True(
                !GetNamedText(view.ServiceView, "ApiStatus")
                    .Contains("正在启动", StringComparison.Ordinal),
                "服务页刷新后设备 API 仍停在初始状态");
            Equal(
                $"进程 {Environment.ProcessId}",
                GetNamedText(view.ServiceView, "ProcessDetail"),
                "PC 后台详情重复显示应用名称");
            True(
                GetNamedText(view.ServiceView, "ApiDetail")
                    .StartsWith("端口 ", StringComparison.Ordinal),
                "设备 API 详情重复显示服务说明");
            True(
                !GetNamedText(view.ServiceView, "CodexDetail")
                    .Contains('\n'),
                "Codex 采集详情不应重复显示字段标题");
            Equal(
                "辽宁·大连",
                GetNamedText(view.ServiceView, "WeatherDetail"),
                "服务页天气位置没有使用最近成功查询的地区");
            AssertControlCenterEventSubscribed(
                view,
                "ClearPairingRequested");
            AssertControlCenterEventSubscribed(
                view,
                "BrightnessChanged");
            AssertControlCenterEventSubscribed(
                view,
                "NightBacklightRequested");
            AssertControlCenterEventSubscribed(
                view,
                "RestartDeviceRequested");
            AssertControlCenterEventSubscribed(
                view,
                "VersionClicked");
            AssertControlCenterEventSubscribed(
                view,
                "DeveloperRequested");
            AssertControlCenterEventSubscribed(
                view,
                "DisableDeveloperRequested");
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
        finally
        {
            presenter?.Dispose();
            runtime?.Dispose();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(
        uiThread.Join(TimeSpan.FromSeconds(10)),
        "原生 WPF 服务操作接线测试超时");
    if (failure != null)
        throw new InvalidOperationException(
            "原生 WPF 服务操作接线测试失败",
            failure);
}

static void AssertControlCenterEventSubscribed(
    LibreHardwareMonitor.UI.WpfViews.SolisControlCenterView view,
    string eventName)
{
    System.Reflection.FieldInfo? eventField = view.GetType().GetField(
        eventName,
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    True(eventField is not null, $"未找到控制中心事件 {eventName}");
    True(
        eventField!.GetValue(view) is Delegate,
        $"控制中心事件 {eventName} 没有订阅者");
}

static string GetNamedText(
    System.Windows.FrameworkElement view,
    string name)
{
    var textBlock = view.FindName(name) as System.Windows.Controls.TextBlock;
    True(textBlock is not null, $"未找到文本控件 {name}");
    return textBlock!.Text;
}

static void RaiseControlCenterEvent(
    LibreHardwareMonitor.UI.WpfViews.SolisControlCenterView view,
    string eventName)
{
    System.Reflection.FieldInfo? eventField = view.GetType().GetField(
        eventName,
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    True(eventField is not null, $"未找到控制中心事件 {eventName}");
    var handler = (EventHandler?)eventField!.GetValue(view);
    True(handler is not null, $"控制中心事件 {eventName} 没有订阅者");
    handler!.Invoke(view, EventArgs.Empty);
}

static void RaiseServiceEvent(
    LibreHardwareMonitor.UI.WpfViews.SolisServiceView view,
    string eventName)
{
    System.Reflection.FieldInfo? eventField = view.GetType().GetField(
        eventName,
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    True(eventField is not null, $"未找到服务页事件 {eventName}");
    var handler = (EventHandler?)eventField!.GetValue(view);
    True(handler is not null, $"服务页事件 {eventName} 没有订阅者");
    handler!.Invoke(view, EventArgs.Empty);
}

static void WpfTaskbarHostUsesNativeWpfNotifyIcon()
{
    System.Reflection.Assembly assembly =
        typeof(LibreHardwareMonitor.UI.TreeModel).Assembly;
    Type? hostType = assembly.GetType(
        "LibreHardwareMonitor.UI.WpfViews.SolisTaskbarHost");
    True(hostType is not null, "未找到 WPF 托盘宿主");
    True(typeof(IDisposable).IsAssignableFrom(hostType!),
        "WPF 托盘宿主必须能够释放托盘资源");

    Type[] fieldTypes = hostType!
        .GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public)
        .Select(field => field.FieldType)
        .ToArray();
    True(fieldTypes.Any(type =>
            type.FullName == "H.NotifyIcon.TaskbarIcon"),
        "WPF 托盘宿主必须使用 H.NotifyIcon.TaskbarIcon");
    True(!fieldTypes.Any(type =>
            type.FullName == "System.Windows.Forms.NotifyIcon" ||
            type.FullName == "LibreHardwareMonitor.UI.NotifyIconAdv" ||
            type.FullName == "LibreHardwareMonitor.UI.SystemTray"),
        "WPF 托盘宿主不得依赖 WinForms 托盘组件");
    True(
        hostType.GetField(
            "_openDeviceWebUiItem",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic) is not null,
        "WPF 托盘菜单缺少显示副屏 WebUI 入口");
}

static void WpfTaskbarIconSourceHasAbsoluteResourceUri()
{
    Type? hostType = typeof(LibreHardwareMonitor.UI.TreeModel).Assembly.GetType(
        "LibreHardwareMonitor.UI.WpfViews.SolisTaskbarHost");
    True(hostType is not null, "未找到 WPF 托盘宿主");

    System.Reflection.MethodInfo? loadIconSource = hostType!.GetMethod(
        "LoadIconSource",
        System.Reflection.BindingFlags.Static |
        System.Reflection.BindingFlags.NonPublic);
    True(loadIconSource is not null, "未找到 WPF 托盘图标加载入口");

    var iconSource = (System.Windows.Media.ImageSource?)
        loadIconSource!.Invoke(null, null);
    True(iconSource is not null, "WPF 托盘图标资源为空");
    True(
        Uri.TryCreate(
            iconSource!.ToString(),
            UriKind.Absolute,
            out Uri? resourceUri) &&
        string.Equals(resourceUri.Scheme, "pack", StringComparison.Ordinal),
        "WPF 托盘图标必须提供 H.NotifyIcon 可解析的绝对 pack URI");
}

static void WpfControlCenterDoesNotBuildHiddenLegacyPages()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            Type? controlType = typeof(LibreHardwareMonitor.UI.TreeModel).Assembly.GetType(
                "LibreHardwareMonitor.UI.SolisControlCenterControl");
            True(controlType is not null, "未找到 Solis 控制中心");

            object[] arguments =
            [
                (Func<SolisMetricsSnapshot>)(() => SolisMetricsSnapshot.Empty),
                (Func<bool>)(() => true),
                (Func<DeviceDiscoveryState>)(() => new DeviceDiscoveryState(
                    null,
                    false,
                    "NotFound")),
                (Func<SolisDiagnosticsSnapshot>)(() => SolisDiagnosticsSnapshot.Initial),
                (Func<DateTimeOffset?>)(() => null),
                (Func<bool>)(() => true),
                (Func<string>)(() => Path.GetTempPath()),
                (Func<QWeatherSettings>)(() => new QWeatherSettings(
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null)),
                (Func<QWeatherSettings, WeatherMetricsReading>)(
                    _ => WeatherMetricsReading.Empty("Disabled")),
                (Action<QWeatherSettings, WeatherMetricsReading>)((_, _) => { }),
                (Func<bool>)(() => false),
                (Action<bool>)(_ => { }),
                (Func<bool>)(() => false),
                (Action<bool>)(_ => { }),
                (Func<string, IProgress<FirmwareUpdateProgress>, Task<FirmwareUpdateResult>>)(
                    (_, _) => Task.FromResult(new FirmwareUpdateResult(false, "未执行"))),
                (Func<Task<DeviceControlResult>>)(() =>
                    Task.FromResult(new DeviceControlResult(false, "未配对"))),
                (Func<DeviceDisplaySettings, Task<DeviceControlResult>>)(_ =>
                    Task.FromResult(new DeviceControlResult(false, "未配对"))),
                (Func<Task<DeviceControlResult>>)(() =>
                    Task.FromResult(new DeviceControlResult(false, "未配对"))),
                (Action)(() => { }),
                (Action)(() => { }),
                (Action)(() => { }),
                false,
                (Action<bool>)(_ => { })
            ];

            using var control = (Control)Activator.CreateInstance(controlType!, arguments)!;
            Equal(1, control.Controls.Count,
                "WPF 正常路径不应创建隐藏的 WinForms 页面树");
            Equal(
                "System.Windows.Forms.Integration.ElementHost",
                control.Controls[0].GetType().FullName,
                "WPF 正常路径根控件应只保留 ElementHost");

            InvokePrivate(controlType!, control, "SetDeveloperModeUnlocked", true);
            InvokePrivate(controlType!, control, "SetDeveloperModeUnlocked", false);
            Equal(1, control.Controls.Count,
                "切换开发者模式后不应补建隐藏的 WinForms 页面树");
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(uiThread.Join(TimeSpan.FromSeconds(15)), "WPF 控制中心隐藏页面测试超时");
    if (failure != null)
        throw new InvalidOperationException("WPF 控制中心隐藏页面测试失败", failure);
}

static void WpfControlCenterAllocatesOnlyElementHostControlField()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            Type? controlType = typeof(LibreHardwareMonitor.UI.TreeModel).Assembly.GetType(
                "LibreHardwareMonitor.UI.SolisControlCenterControl");
            True(controlType is not null, "未找到 Solis 控制中心");

            object[] arguments =
            [
                (Func<SolisMetricsSnapshot>)(() => SolisMetricsSnapshot.Empty),
                (Func<bool>)(() => true),
                (Func<DeviceDiscoveryState>)(() => new DeviceDiscoveryState(
                    null,
                    false,
                    "NotFound")),
                (Func<SolisDiagnosticsSnapshot>)(() => SolisDiagnosticsSnapshot.Initial),
                (Func<DateTimeOffset?>)(() => null),
                (Func<bool>)(() => true),
                (Func<string>)(() => Path.GetTempPath()),
                (Func<QWeatherSettings>)(() => new QWeatherSettings(
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null)),
                (Func<QWeatherSettings, WeatherMetricsReading>)(
                    _ => WeatherMetricsReading.Empty("Disabled")),
                (Action<QWeatherSettings, WeatherMetricsReading>)((_, _) => { }),
                (Func<bool>)(() => false),
                (Action<bool>)(_ => { }),
                (Func<bool>)(() => false),
                (Action<bool>)(_ => { }),
                (Func<string, IProgress<FirmwareUpdateProgress>, Task<FirmwareUpdateResult>>)(
                    (_, _) => Task.FromResult(new FirmwareUpdateResult(false, "未执行"))),
                (Func<Task<DeviceControlResult>>)(() =>
                    Task.FromResult(new DeviceControlResult(false, "未配对"))),
                (Func<DeviceDisplaySettings, Task<DeviceControlResult>>)(_ =>
                    Task.FromResult(new DeviceControlResult(false, "未配对"))),
                (Func<Task<DeviceControlResult>>)(() =>
                    Task.FromResult(new DeviceControlResult(false, "未配对"))),
                (Action)(() => { }),
                (Action)(() => { }),
                (Action)(() => { }),
                false,
                (Action<bool>)(_ => { })
            ];

            using var control = (Control)Activator.CreateInstance(controlType!, arguments)!;
            string[] allocatedControlFields = controlType!
                .GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                .Where(field =>
                    typeof(Control).IsAssignableFrom(field.FieldType) &&
                    field.GetValue(control) is Control)
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Equal(
                "_wpfShellHost",
                string.Join(", ", allocatedControlFields),
                "WPF 控制中心不应分配隐藏的 WinForms 兼容控件");
        }
        catch (Exception exception)
        {
            failure = UnwrapInvocationException(exception);
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(uiThread.Join(TimeSpan.FromSeconds(15)), "WPF 控制中心控件分配测试超时");
    if (failure != null)
        throw new InvalidOperationException("WPF 控制中心控件分配测试失败", failure);
}

static void TreeModelChangesAreMarshaledToUiThread()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        try
        {
            using var tree = new TreeViewAdv();
            _ = tree.Handle;

            var model = new LibreHardwareMonitor.UI.TreeModel();
            tree.Model = model;
            var adapter = new LibreHardwareMonitor.UI.Node("adapter");

            Task.Run(() => model.Nodes.Add(adapter)).GetAwaiter().GetResult();
            Equal(0, tree.Root.Children.Count,
                "后台线程不应直接修改 TreeViewAdv 的内部节点集合");

            PumpWindowsMessagesUntil(() => tree.Root.Children.Count == 1);
            Equal(1, tree.Root.Children.Count,
                "UI 线程处理消息后应显示新增节点");

            Task.Run(() => model.Nodes.Remove(adapter)).GetAwaiter().GetResult();
            Equal(1, tree.Root.Children.Count,
                "后台线程不应直接移除 TreeViewAdv 的内部节点");

            PumpWindowsMessagesUntil(() => tree.Root.Children.Count == 0);
            Equal(0, tree.Root.Children.Count,
                "UI 线程处理消息后应移除节点");
        }
        catch (Exception exception)
        {
            failure = exception;
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(uiThread.Join(TimeSpan.FromSeconds(10)),
        "传感器树线程测试超时");

    if (failure != null)
        throw new InvalidOperationException("传感器树线程测试失败", failure);
}

static void TreeNetworkHotplugSurvivesUiStateChanges()
{
    Exception? failure = null;
    var uiThread = new Thread(() =>
    {
        ThreadExceptionEventHandler handler = (_, args) =>
            failure ??= args.Exception;
        Application.ThreadException += handler;
        try
        {
            using var host = new Panel();
            using var controlCenter = new Panel { Dock = DockStyle.Fill };
            using var tree = new TreeViewAdv { Dock = DockStyle.Fill };
            host.Controls.Add(tree);
            host.Controls.Add(controlCenter);
            _ = host.Handle;
            _ = controlCenter.Handle;
            _ = tree.Handle;

            var model = new LibreHardwareMonitor.UI.TreeModel();
            tree.Model = model;

            ExerciseTreeHotplugState(
                "控制中心可见",
                host,
                controlCenter,
                tree,
                model,
                hostVisible: true,
                controlCenterVisible: true,
                treeVisible: false);
            ExerciseTreeHotplugState(
                "主窗口与原始树隐藏",
                host,
                controlCenter,
                tree,
                model,
                hostVisible: false,
                controlCenterVisible: false,
                treeVisible: false);
            ExerciseTreeHotplugState(
                "开发者模式原始树可见",
                host,
                controlCenter,
                tree,
                model,
                hostVisible: true,
                controlCenterVisible: false,
                treeVisible: true);
            if (failure != null)
                throw failure;
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
        finally
        {
            Application.ThreadException -= handler;
        }
    });

    uiThread.IsBackground = true;
    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    True(uiThread.Join(TimeSpan.FromSeconds(15)),
        "网卡热插拔界面状态测试超时");

    if (failure != null)
        throw new InvalidOperationException(
            "网卡热插拔界面状态测试失败",
            failure);
}

static void ExerciseTreeHotplugState(
    string stateName,
    Panel host,
    Panel controlCenter,
    TreeViewAdv tree,
    LibreHardwareMonitor.UI.TreeModel model,
    bool hostVisible,
    bool controlCenterVisible,
    bool treeVisible)
{
    host.Visible = hostVisible;
    controlCenter.Visible = controlCenterVisible;
    tree.Visible = treeVisible;

    Task changes = Task.Run(() =>
    {
        for (int index = 0; index < 24; index++)
        {
            var adapter =
                new LibreHardwareMonitor.UI.Node(
                    $"{stateName}-adapter-{index}");
            model.Nodes.Add(adapter);
            model.Nodes.Remove(adapter);
        }
    });

    var timeout = Stopwatch.StartNew();
    while (!changes.IsCompleted &&
           timeout.Elapsed < TimeSpan.FromSeconds(5))
    {
        Application.DoEvents();
        _ = tree.AllNodes.Count();
        Thread.Yield();
    }
    changes.GetAwaiter().GetResult();
    PumpWindowsMessagesUntil(() => tree.Root.Children.Count == 0);
    Equal(0, tree.Root.Children.Count,
        $"{stateName}结束后残留网卡节点");

    var finalAdapter =
        new LibreHardwareMonitor.UI.Node($"{stateName}-final");
    Task.Run(() => model.Nodes.Add(finalAdapter)).GetAwaiter().GetResult();
    PumpWindowsMessagesUntil(() => tree.Root.Children.Count == 1);
    Equal(1, tree.Root.Children.Count,
        $"{stateName}无法显示重新加入的网卡节点");
    Task.Run(() => model.Nodes.Remove(finalAdapter)).GetAwaiter().GetResult();
    PumpWindowsMessagesUntil(() => tree.Root.Children.Count == 0);
    Equal(0, tree.Root.Children.Count,
        $"{stateName}无法移除重新加入的网卡节点");
}

static void PumpWindowsMessagesUntil(Func<bool> condition)
{
    var timeout = Stopwatch.StartNew();
    while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(2))
    {
        Application.DoEvents();
        Thread.Yield();
    }
}

static T GetPrivateField<T>(Type type, object instance, string name)
{
    System.Reflection.FieldInfo? field = type.GetField(
        name,
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    True(field is not null, $"未找到字段 {name}");
    return (T)field!.GetValue(instance)!;
}

static T GetPublicProperty<T>(Type type, object instance, string name)
{
    System.Reflection.PropertyInfo? property = type.GetProperty(name);
    True(property is not null, $"未找到属性 {name}");
    return (T)property!.GetValue(instance)!;
}

static void InvokePrivate(
    Type type,
    object instance,
    string name,
    params object?[] arguments)
{
    System.Reflection.MethodInfo? method = type.GetMethod(
        name,
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    True(method is not null, $"未找到方法 {name}");
    method!.Invoke(instance, arguments);
}

static Exception UnwrapInvocationException(Exception exception) =>
    exception is System.Reflection.TargetInvocationException
        { InnerException: not null }
        ? exception.InnerException
        : exception;
}
