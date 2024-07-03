using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Popups;
using System.Text;

namespace Draggable;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    int m_width = 245;
    int m_height = 270; // including titlebar
    Window? m_window;
    public static bool IsClosing { get; set; } = false;
    public static IntPtr WindowHandle { get; set; }
    public static FrameworkElement? MainRoot { get; set; }

    static bool _lastSave = false;
    static DateTime _lastMove = DateTime.Now;
    static Config? _localConfig;
    public static Config? LocalConfig
    {
        get => _localConfig;
        set => _localConfig = value;
    }

    public static Func<Config?, bool> SaveConfigFunc = (cfg) =>
    {
        if (cfg is not null)
        {
            cfg.version = $"{GetCurrentAssemblyVersion()}";
            if (cfg.opacity == 0.0) { cfg.opacity = 0.1; }
            Process proc = Process.GetCurrentProcess();
            cfg.metrics = $"Process used {proc.PrivateMemorySize64 / 1024 / 1024}MB of memory and {proc.TotalProcessorTime.ToReadableString()} TotalProcessorTime on {Environment.ProcessorCount} possible cores.";
            try
            {
                _ = ConfigHelper.SaveConfig(cfg);
                return true;
            }
            catch (Exception) { return false; }
        }
        return false;
    };

    // https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/#advantages-and-disadvantages-of-packaging-your-app
#if IS_UNPACKAGED // We're using a custom PropertyGroup Condition we defined in the csproj to help us with the decision.
    public static bool IsPackaged { get => false; }
#else
public static bool IsPackaged { get => true; }
#endif
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        Debug.WriteLine($"[INFO] {System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType?.Name}__{System.Reflection.MethodBase.GetCurrentMethod()?.Name} [{DateTime.Now.ToString("hh:mm:ss.fff tt")}]");

        App.Current.DebugSettings.FailFastOnErrors = false;

        #region [Exception handlers]
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainUnhandledException;
        AppDomain.CurrentDomain.FirstChanceException += CurrentDomainFirstChanceException;
        AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        UnhandledException += ApplicationUnhandledException;
        #endregion

        this.InitializeComponent();

        foreach (var ra in GetReferencedAssemblies())
        {
            Debug.WriteLine($"[INFO] {ra.Key} {ra.Value}");
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        #region [Config]
        if (ConfigHelper.DoesConfigExist())
        {
            try
            {
                LocalConfig = ConfigHelper.LoadConfig();
                // If we started from the registry, then set the current directory to the application's directory.
                if (LocalConfig != null && !IsPackaged)
                {
                    var exePath = Assembly.GetExecutingAssembly().Location;
                    var exeDirectory = System.IO.Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrEmpty(exeDirectory))
                    {
                        Directory.SetCurrentDirectory(exeDirectory);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"[ERROR] {nameof(ConfigHelper.LoadConfig)}: {ex.Message}");
            }
        }
        else
        {
            try
            {
                LocalConfig = new Config
                {
                    version = $"{App.GetCurrentAssemblyVersion()}",
                    metrics = "N/A",
                    opacity = 0.6,
                    gradientLength = 0.5,
                    gradientDarken = true,
                    windowW = m_width,
                    windowH = m_height,
                    clockFace = "Clockface1c",
                    randomHands = false,
                    hourColor = "4169E1",   // blue
                    minuteColor = "404040", // dark gray
                    secondColor = "B22222"  // red
                };
                ConfigHelper.SaveConfig(LocalConfig);
            }
            catch (Exception ex)
            {
                DebugLog($"[ERROR] {nameof(ConfigHelper.SaveConfig)}: {ex.Message}");
            }
        }
        #endregion

        #region [AppActivationArguments]
        var appInst = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent();
        if (appInst != null)
        {
            var activationArguments = appInst.GetActivatedEventArgs();
            DebugLog($"Activation kind: {activationArguments.Kind}");
            
            if(activationArguments.Data is Windows.ApplicationModel.Activation.IToastNotificationActivatedEventArgs tnaea)
                Debug.WriteLine($"[INFO] IToastNotificationActivatedEventArgs: {tnaea.UserInput.Count}");
            if (activationArguments.Data is Windows.ApplicationModel.Activation.IStartupTaskActivatedEventArgs staea)
                Debug.WriteLine($"[INFO] IStartupTaskActivatedEventArgs: {staea.TaskId}");
            if (activationArguments.Data is Windows.ApplicationModel.Activation.IBackgroundActivatedEventArgs baea)
                Debug.WriteLine($"[INFO] IBackgroundActivatedEventArgs: {baea.TaskInstance.SuspendedCount}");
            if (activationArguments.Data is Windows.ApplicationModel.Activation.IApplicationViewActivatedEventArgs avaea)
                Debug.WriteLine($"[INFO] IApplicationViewActivatedEventArgs: {avaea.CurrentlyShownApplicationViewId}");
            if (activationArguments.Data is Windows.ApplicationModel.Activation.IActivatedEventArgsWithUser aeawu)
                Debug.WriteLine($"[INFO] IActivatedEventArgsWithUser: {aeawu.User.Type}");
            if (activationArguments.Data is Windows.ApplicationModel.Activation.IDeviceActivatedEventArgs daea)
                Debug.WriteLine($"[INFO] IDeviceActivatedEventArgs: {daea.DeviceInformationId}");
            if (activationArguments.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs faea)
                Debug.WriteLine($"[INFO] IFileActivatedEventArgs: {faea.Files.Count}");
            if (activationArguments.Data is Windows.ApplicationModel.Activation.ISearchActivatedEventArgs saea)
                Debug.WriteLine($"[INFO] ISearchActivatedEventArgs: {saea.QueryText}");

            if (activationArguments.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs laea)
            {
                if (laea.PreviousExecutionState == ApplicationExecutionState.NotRunning || laea.PreviousExecutionState == ApplicationExecutionState.Terminated)
                    DebugLog($"The app did not close normally: {laea.PreviousExecutionState}");
                else
                    DebugLog($"The app closed normally: {laea.PreviousExecutionState}");
            }
            else if (activationArguments.Data is Windows.ApplicationModel.Activation.IActivatedEventArgs aea)
            {
                if (aea.PreviousExecutionState == ApplicationExecutionState.NotRunning || aea.PreviousExecutionState == ApplicationExecutionState.Terminated)
                    DebugLog($"The app did not close normally: {aea.PreviousExecutionState}");
                else
                    DebugLog($"The app closed normally: {aea.PreviousExecutionState}");
            }
        }
        #endregion

        m_window = new MainWindow();
        var appWin = GetAppWindow(m_window);
        if (appWin != null)
        {
            // Gets or sets a value that indicates whether this window will appear in various system representations, such as ALT+TAB and taskbar.
            appWin.IsShownInSwitchers = true;

            // We don't have the Closing event exposed by default, so we'll use the AppWindow to compensate.
            appWin.Closing += (s, e) =>
            {
                App.IsClosing = true;
                Debug.WriteLine($"[INFO] Application closing detected at {DateTime.Now.ToString("hh:mm:ss.fff tt")}");
                _lastSave = SaveConfigFunc(LocalConfig);
            };

            // Destroying is always called, but Closing is only called when the application is shutdown normally.
            appWin.Destroying += (s, e) =>
            {
                Debug.WriteLine($"[INFO] Application destroying detected at {DateTime.Now.ToString("hh:mm:ss.fff tt")}");
                if (!_lastSave) // prevent redundant calls
                    SaveConfigFunc(LocalConfig);
            };

            // The changed event holds a bunch of juicy info that we can extrapolate.
            appWin.Changed += (s, args) =>
            {
                if (args.DidSizeChange)
                {
                    Debug.WriteLine($"[INFO] Window size changed to {s.Size.Width},{s.Size.Height}");
                }

                if (args.DidPositionChange)
                {
                    // Add debounce in scenarios where this event could be hammered.
                    var idleTime = DateTime.Now - _lastMove;
                    if (idleTime.TotalSeconds > 0.5d && LocalConfig != null)
                    {
                        _lastMove = DateTime.Now;
                        if (s.Position.X > 0 && s.Position.Y > 0 && s.Size.Height > 0 && s.Size.Width > 0)
                        {
                            // This property is initially null. Once a window has been shown it always has a
                            // presenter applied, either one applied by the platform or applied by the app itself.
                            if (s.Presenter is not null && s.Presenter is OverlappedPresenter op)
                            {
                                if (op.State == OverlappedPresenterState.Minimized)
                                {
                                    //appWin.IsShownInSwitchers = true;
                                    Debug.WriteLine($"[INFO] Window minimized");
                                }
                                else if (op.State != OverlappedPresenterState.Maximized)
                                {
                                    //appWin.IsShownInSwitchers = false;
                                    Debug.WriteLine($"[INFO] Updating window position to {s.Position.X},{s.Position.Y} and size to {s.Size.Width},{s.Size.Height}");
                                    LocalConfig.windowX = s.Position.X;
                                    LocalConfig.windowY = s.Position.Y;
                                    LocalConfig.windowH = s.Size.Height;
                                    LocalConfig.windowW = s.Size.Width;
                                }
                                else
                                {
                                    //appWin.IsShownInSwitchers = false;
                                    Debug.WriteLine($"[INFO] Ignoring position saving (window maximized or restored)");
                                }
                            }
                        }
                    }
                }
            };

            // Set the application icon.
            if (IsPackaged)
                appWin.SetIcon(System.IO.Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, $"Assets/StoreLogo.ico"));
            else
                appWin.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, $"Assets/StoreLogo.ico"));

            appWin.TitleBar.IconShowOptions = IconShowOptions.ShowIconAndSystemMenu;
        }

        m_window.Activate();

        // Save the FrameworkElement for any future content dialogs.
        MainRoot = m_window.Content as FrameworkElement;

        #region [Window placement]
        if (LocalConfig == null)
        {
            Debug.WriteLine($"[INFO] Moving window to center screen");
            appWin?.Resize(new Windows.Graphics.SizeInt32(m_width, m_height));
            CenterWindow(m_window);
        }
        else
        {
            Debug.WriteLine($"[INFO] Moving window to previous position {LocalConfig.windowX},{LocalConfig.windowY} with size {LocalConfig.windowW},{LocalConfig.windowH}");
            appWin?.MoveAndResize(new Windows.Graphics.RectInt32(LocalConfig.windowX, LocalConfig.windowY, LocalConfig.windowW, LocalConfig.windowH), Microsoft.UI.Windowing.DisplayArea.Primary);
        }
        #endregion
    }

    #region [Window Helpers]
    /// <summary>
    /// This code example demonstrates how to retrieve an AppWindow from a WinUI3 window.
    /// The AppWindow class is available for any top-level HWND in your app.
    /// AppWindow is available only to desktop apps (both packaged and unpackaged), it's not available to UWP apps.
    /// https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/windowing/windowing-overview
    /// https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.create?view=windows-app-sdk-1.3
    /// </summary>
    public Microsoft.UI.Windowing.AppWindow? GetAppWindow(object window)
    {
        // Retrieve the window handle (HWND) of the current (XAML) WinUI3 window.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        // For other classes to use (mostly P/Invoke).
        App.WindowHandle = hWnd;

        // Retrieve the WindowId that corresponds to hWnd.
        Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);

        // Lastly, retrieve the AppWindow for the current (XAML) WinUI3 window.
        Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        return appWindow;
    }

    /// <summary>
    /// Centers a <see cref="Microsoft.UI.Xaml.Window"/> based on the <see cref="Microsoft.UI.Windowing.DisplayArea"/>.
    /// </summary>
    /// <remarks>This must be run on the UI thread.</remarks>
    public static void CenterWindow(Window window)
    {
        if (window == null) { return; }

        try
        {
            System.IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            if (Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId) is Microsoft.UI.Windowing.AppWindow appWindow &&
                Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest) is Microsoft.UI.Windowing.DisplayArea displayArea)
            {
                Windows.Graphics.PointInt32 CenteredPosition = appWindow.Position;
                CenteredPosition.X = (displayArea.WorkArea.Width - appWindow.Size.Width) / 2;
                CenteredPosition.Y = (displayArea.WorkArea.Height - appWindow.Size.Height) / 2;
                appWindow.Move(CenteredPosition);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] {MethodBase.GetCurrentMethod()?.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The <see cref="Microsoft.UI.Windowing.DisplayArea"/> exposes properties such as:
    /// OuterBounds     (Rect32)
    /// WorkArea.Width  (int)
    /// WorkArea.Height (int)
    /// IsPrimary       (bool)
    /// DisplayId.Value (ulong)
    /// </summary>
    /// <param name="window"></param>
    /// <returns><see cref="DisplayArea"/></returns>
    public Microsoft.UI.Windowing.DisplayArea? GetDisplayArea(Window window)
    {
        try
        {
            System.IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var da = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            return da;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] {MethodBase.GetCurrentMethod()?.Name}: {ex.Message}");
            return null;
        }
    }
    #endregion

    #region [Reflection Helpers]
    /// <summary>
    /// Returns the declaring type's namespace.
    /// </summary>
    public static string? GetCurrentNamespace() => System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType?.Namespace;

    /// <summary>
    /// Returns the declaring type's full name.
    /// </summary>
    public static string? GetCurrentFullName() => System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType?.Assembly.FullName;

    /// <summary>
    /// Returns the declaring type's assembly name.
    /// </summary>
    public static string? GetCurrentAssemblyName() => System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;

    /// <summary>
    /// Returns the AssemblyVersion, not the FileVersion.
    /// </summary>
    public static Version GetCurrentAssemblyVersion() => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version();
    #endregion

    #region [Dialog Helpers]
    static SemaphoreSlim semaSlim = new SemaphoreSlim(1, 1);
    /// <summary>
    /// The <see cref="Windows.UI.Popups.MessageDialog"/> does not look as nice as the
    /// <see cref="Microsoft.UI.Xaml.Controls.ContentDialog"/> and is not part of the native Microsoft.UI.Xaml.Controls.
    /// The <see cref="Windows.UI.Popups.MessageDialog"/> offers the <see cref="Windows.UI.Popups.UICommandInvokedHandler"/> 
    /// callback, but this could be replaced with actions. Both can be shown asynchronously.
    /// </summary>
    /// <remarks>
    /// You'll need to call <see cref="WinRT.Interop.InitializeWithWindow.Initialize"/> when using the <see cref="Windows.UI.Popups.MessageDialog"/>,
    /// because the <see cref="Microsoft.UI.Xaml.XamlRoot"/> does not exist and an owner must be defined.
    /// </remarks>
    public static async Task ShowMessageBox(string title, string message, string yesText, string noText, Action? yesAction, Action? noAction)
    {
        if (App.WindowHandle == IntPtr.Zero) { return; }

        // Create the dialog.
        var messageDialog = new MessageDialog($"{message}");
        messageDialog.Title = title;

        if (!string.IsNullOrEmpty(yesText))
        {
            messageDialog.Commands.Add(new UICommand($"{yesText}", (opt) => { yesAction?.Invoke(); }));
            messageDialog.DefaultCommandIndex = 0;
        }

        if (!string.IsNullOrEmpty(noText))
        {
            messageDialog.Commands.Add(new UICommand($"{noText}", (opt) => { noAction?.Invoke(); }));
            messageDialog.DefaultCommandIndex = 1;
        }

        // We must initialize the dialog with an owner.
        WinRT.Interop.InitializeWithWindow.Initialize(messageDialog, App.WindowHandle);
        // Show the message dialog. Our DialogDismissedHandler will deal with what selection the user wants.
        await messageDialog.ShowAsync();
        // We could force the result in a separate timer...
        //DialogDismissedHandler(new UICommand("time-out"));
    }

    /// <summary>
    /// The <see cref="Windows.UI.Popups.MessageDialog"/> does not look as nice as the
    /// <see cref="Microsoft.UI.Xaml.Controls.ContentDialog"/> and is not part of the native Microsoft.UI.Xaml.Controls.
    /// The <see cref="Windows.UI.Popups.MessageDialog"/> offers the <see cref="Windows.UI.Popups.UICommandInvokedHandler"/> 
    /// callback, but this could be replaced with actions. Both can be shown asynchronously.
    /// </summary>
    /// <remarks>
    /// You'll need to call <see cref="WinRT.Interop.InitializeWithWindow.Initialize"/> when using the <see cref="Windows.UI.Popups.MessageDialog"/>,
    /// because the <see cref="Microsoft.UI.Xaml.XamlRoot"/> does not exist and an owner must be defined.
    /// </remarks>
    public static async Task ShowMessageBox(string title, string message, string primaryText, string cancelText)
    {
        // Create the dialog.
        var messageDialog = new MessageDialog($"{message}");
        messageDialog.Title = title;

        if (!string.IsNullOrEmpty(primaryText))
        {
            messageDialog.Commands.Add(new UICommand($"{primaryText}", new UICommandInvokedHandler(DialogDismissedHandler)));
            messageDialog.DefaultCommandIndex = 0;
        }

        if (!string.IsNullOrEmpty(cancelText))
        {
            messageDialog.Commands.Add(new UICommand($"{cancelText}", new UICommandInvokedHandler(DialogDismissedHandler)));
            messageDialog.DefaultCommandIndex = 1;
        }
        // We must initialize the dialog with an owner.
        WinRT.Interop.InitializeWithWindow.Initialize(messageDialog, App.WindowHandle);
        // Show the message dialog. Our DialogDismissedHandler will deal with what selection the user wants.
        await messageDialog.ShowAsync();

        // We could force the result in a separate timer...
        //DialogDismissedHandler(new UICommand("time-out"));
    }

    /// <summary>
    /// Callback for the selected option from the user.
    /// </summary>
    static void DialogDismissedHandler(IUICommand command)
    {
        Debug.WriteLine($"[INFO] UICommand.Label ⇨ {command.Label}");
    }

    /// <summary>
    /// The <see cref="Microsoft.UI.Xaml.Controls.ContentDialog"/> looks much better than the
    /// <see cref="Windows.UI.Popups.MessageDialog"/> and is part of the native Microsoft.UI.Xaml.Controls.
    /// The <see cref="Microsoft.UI.Xaml.Controls.ContentDialog"/> does not offer a <see cref="Windows.UI.Popups.UICommandInvokedHandler"/>
    /// callback, but in this example was replaced with actions. Both can be shown asynchronously.
    /// </summary>
    /// <remarks>
    /// There is no need to call <see cref="WinRT.Interop.InitializeWithWindow.Initialize"/> when using the <see cref="Microsoft.UI.Xaml.Controls.ContentDialog"/>,
    /// but a <see cref="Microsoft.UI.Xaml.XamlRoot"/> must be defined since it inherits from <see cref="Microsoft.UI.Xaml.Controls.Control"/>.
    /// The <see cref="SemaphoreSlim"/> was added to prevent "COMException: Only one ContentDialog can be opened at a time."
    /// </remarks>
    public static async Task ShowDialogBox(string title, string message, string primaryText, string cancelText, Action? onPrimary, Action? onCancel, Uri? imageUri)
    {
        if (App.MainRoot?.XamlRoot == null) { return; }

        await semaSlim.WaitAsync();

        #region [Initialize Assets]
        double fontSize = 16;
        Microsoft.UI.Xaml.Media.FontFamily fontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");

        if (App.Current.Resources.TryGetValue("FontSizeMedium", out object _))
            fontSize = (double)App.Current.Resources["FontSizeMedium"];

        if (App.Current.Resources.TryGetValue("PrimaryFont", out object _))
            fontFamily = (Microsoft.UI.Xaml.Media.FontFamily)App.Current.Resources["PrimaryFont"];

        StackPanel panel = new StackPanel()
        {
            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Vertical,
            Spacing = 10d
        };

        if (imageUri is not null)
        {
            panel.Children.Add(new Image
            {
                Margin = new Thickness(1, -50, 1, 1), // Move the image into the title area.
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                Width = 40,
                Height = 40,
                Source = new BitmapImage(imageUri)
            });
        }

        panel.Children.Add(new TextBlock()
        {
            Text = message,
            FontSize = fontSize,
            FontFamily = fontFamily,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
        });

        var tb = new TextBox()
        {
            Text = message,
            FontSize = fontSize,
            FontFamily = fontFamily,
            TextWrapping = TextWrapping.Wrap
        };
        tb.Loaded += (s, e) => { tb.SelectAll(); };
        #endregion

        // NOTE: Content dialogs will automatically darken the background.
        ContentDialog contentDialog = new ContentDialog()
        {
            Title = title,
            PrimaryButtonText = primaryText,
            CloseButtonText = cancelText,
            Content = panel,
            XamlRoot = App.MainRoot?.XamlRoot,
            RequestedTheme = App.MainRoot?.ActualTheme ?? ElementTheme.Default
        };

        try
        {
            ContentDialogResult result = await contentDialog.ShowAsync();

            switch (result)
            {
                case ContentDialogResult.Primary:
                    onPrimary?.Invoke();
                    break;
                //case ContentDialogResult.Secondary:
                //    onSecondary?.Invoke();
                //    break;
                case ContentDialogResult.None: // Cancel
                    onCancel?.Invoke();
                    break;
                default:
                    Debug.WriteLine($"Dialog result not defined.");
                    break;
            }
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            Debug.WriteLine($"[ERROR] ShowDialogBox: {ex.Message}");
        }
        finally
        {
            semaSlim.Release();
        }
    }
    #endregion

    #region [Domain Events]
    void ApplicationUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // https://docs.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.application.unhandledexception.
        Exception? ex = e.Exception;
        Debug.WriteLine($"[UnhandledException]: {ex?.Message}");
        Debug.WriteLine($"Unhandled exception of type {ex?.GetType()}: {ex}");
        DebugLog($"Unhandled Exception StackTrace: {Environment.StackTrace}");
        e.Handled = true;
    }

    void CurrentDomainOnProcessExit(object? sender, EventArgs e)
    {
        if (!IsClosing)
            IsClosing = true;

        if (sender is null)
            return;

        if (sender is AppDomain ad)
        {
            Debug.WriteLine($"[OnProcessExit]", $"{nameof(App)}");
            Debug.WriteLine($"DomainID: {ad.Id}", $"{nameof(App)}");
            Debug.WriteLine($"FriendlyName: {ad.FriendlyName}", $"{nameof(App)}");
            Debug.WriteLine($"BaseDirectory: {ad.BaseDirectory}", $"{nameof(App)}");
        }
    }

    void CurrentDomainFirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        Debug.WriteLine($"[ERROR] First chance exception from {sender?.GetType()}: {e.Exception.Message}");
        DebugLog($"First chance exception from {sender?.GetType()}: {e.Exception.Message}");
        if (e.Exception.InnerException != null)
            DebugLog($"  ⇨ InnerException: {e.Exception.InnerException.Message}");
        DebugLog($"First chance exception StackTrace: {Environment.StackTrace}");
    }

    void CurrentDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        Exception? ex = e.ExceptionObject as Exception;
        Debug.WriteLine($"[ERROR] Thread exception of type {ex?.GetType()}: {ex}");
        DebugLog($"Thread exception of type {ex?.GetType()}: {ex}");
    }

    void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (e.Exception is AggregateException aex)
        {
            aex?.Flatten().Handle(ex =>
            {
                Debug.WriteLine($"[ERROR] Unobserved task exception: {ex?.Message}");
                DebugLog($"Unobserved task exception: {ex?.Message}");
                return true;
            });
        }
        e.SetObserved(); // suppress and handle manually
    }
    #endregion

    #region [Miscellaneous]
    public static void CloseAllDialogs()
    {
        if (App.MainRoot?.XamlRoot == null) { return; }
        var openedDialogs = VisualTreeHelper.GetOpenPopupsForXamlRoot(App.MainRoot?.XamlRoot);
        foreach (var item in openedDialogs)
        {
            if (item.Child is ContentDialog dialog)
                dialog.Hide();
        }
    }

    /// <summary>
    /// Simplified debug logger for app-wide use.
    /// </summary>
    /// <param name="message">the text to append to the file</param>
    public static void DebugLog(string message)
    {
        try
        {
            if (App.IsPackaged)
                System.IO.File.AppendAllText(System.IO.Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Debug.log"), $"[{DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff tt")}] {message}{Environment.NewLine}");
            else
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Debug.log"), $"[{DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff tt")}] {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            Debug.WriteLine($"[{DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.fff tt")}] {message}");
        }
    }

    /// <summary>
    /// Fetching framework version via primitive.
    /// </summary>
    public string ReflectPrimitive()
    {
        StringBuilder sb = new StringBuilder();
        try
        {
            Assembly info = typeof(int).Assembly;
            var sig = info.Location;
            string[] separators = { "\\", "/" };
            var list = sig.Split(separators, StringSplitOptions.RemoveEmptyEntries).Skip(4);
            sb.Append($"• ");
            foreach (var str in list) { sb.Append($"{str} • "); }
        }
        catch (Exception) { }
        return $"{sb}";
    }

    /// <summary>
    /// Returns an exhaustive list of all modules involved in the current process.
    /// </summary>
    public static List<string> GetProcessDependencies()
    {
        List<string> result = new List<string>();
        try
        {
            string self = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".exe";
            System.Diagnostics.ProcessModuleCollection pmc = System.Diagnostics.Process.GetCurrentProcess().Modules;
            IOrderedEnumerable<System.Diagnostics.ProcessModule> pmQuery = pmc
                .OfType<System.Diagnostics.ProcessModule>()
                .Where(pt => pt.ModuleMemorySize > 0)
                .OrderBy(o => o.ModuleName);
            foreach (var item in pmQuery)
            {
                //if (!item.ModuleName.Contains($"{self}"))
                result.Add($"Module name: {item.ModuleName}, {(string.IsNullOrEmpty(item.FileVersionInfo.FileVersion) ? "version unknown" : $"v{item.FileVersionInfo.FileVersion}")}");
                try { item.Dispose(); }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] ProcessModuleCollection: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Returns the basic assemblies needed by the application.
    /// </summary>
    public static Dictionary<string, Version> GetReferencedAssemblies(bool addSelf = false)
    {
        Dictionary<string, Version> values = new Dictionary<string, Version>();
        try
        {
            var assem = Assembly.GetExecutingAssembly();
            int idx = 0; // to prevent key collisions only
            if (addSelf)
                values.Add($"{(++idx).AddOrdinal()}: {assem.GetName().Name}", assem.GetName().Version ?? new Version()); // add self
            IOrderedEnumerable<AssemblyName> names = assem.GetReferencedAssemblies().OrderBy(o => o.Name);
            foreach (var sas in names)
            {
                values.Add($"{(++idx).AddOrdinal()}: {sas.Name}", sas.Version ?? new Version());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] GetReferencedAssemblies: {ex.Message}");
        }
        return values;
    }
    #endregion
}
