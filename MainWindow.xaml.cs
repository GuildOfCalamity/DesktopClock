using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.ApplicationModel;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;

using WinRT.Interop;


namespace Draggable;

public sealed partial class MainWindow : Window
{
    int assetIndex = 0;
    bool useRoundedHands = false;
    bool showMessages = false;
    bool initialized = false;
    DispatcherTimer tmrClock;
    List<string> clockAssets = new();
    byte[] rndBytes = new byte[3];
    SolidColorBrush scbHour = new SolidColorBrush(Microsoft.UI.Colors.RoyalBlue);
    SolidColorBrush scbMinute = new SolidColorBrush(Microsoft.UI.Colors.Gray);
    SolidColorBrush scbSecond = new SolidColorBrush(Microsoft.UI.Colors.Firebrick);

    #region [Testing Borderless]
    static int GWL_STYLE = -16;          // message for title bar's style
    static uint WS_SIZEBOX = 0x00040000;
    static int WS_DLGFRAME = 0x00400000; // window with double border but no title
    static int WS_BORDER = 0x00800000;   // window with border
    static int WS_CAPTION = WS_BORDER | WS_DLGFRAME; // window with a title bar
    #endregion

    #region [Transparency Props]
    Windows.Win32.Foundation.HWND Handle;
    Windows.Win32.UI.WindowsAndMessaging.WINDOW_EX_STYLE WinExStyle
    {
        get => (Windows.Win32.UI.WindowsAndMessaging.WINDOW_EX_STYLE)Windows.Win32.PInvoke.GetWindowLong(Handle, Windows.Win32.UI.WindowsAndMessaging.WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        set => _ = Windows.Win32.PInvoke.SetWindowLong(Handle, Windows.Win32.UI.WindowsAndMessaging.WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)value);
    }
    #endregion

    #region [Dragging Props]
    int initialPointerX = 0;
    int initialPointerY = 0;
    int windowStartX = 0;
    int windowStartY = 0;
    bool isMoving = false;
    Microsoft.UI.Windowing.AppWindow appW;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetCursorPos(out Windows.Graphics.PointInt32 lpPoint);
    #endregion

    public MainWindow()
    {
        this.InitializeComponent();
        this.Activated += MainWindow_Activated;
        this.Title = $"{App.GetCurrentAssemblyName()}";

        #region [Transparency]
        var hwnd = WindowNative.GetWindowHandle(this);
        Handle = new Windows.Win32.Foundation.HWND(hwnd);
        WinExStyle |= Windows.Win32.UI.WindowsAndMessaging.WINDOW_EX_STYLE.WS_EX_LAYERED; // We'll use WS_EX_LAYERED, not WS_EX_TRANSPARENT, for the effect.
        if (App.LocalConfig!.hideTaskbar)
        {
            WinExStyle |= Windows.Win32.UI.WindowsAndMessaging.WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
            // There are also the AppWindow properties that can be used:
            // - AppWindow.IsShownInSwitchers = false
            // - AppWindow.TitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu
        }
        else // We want the user to be able to close the app from the taskbar, so we'll forbear the tool window.
        {
            Debug.WriteLine($"Skipping STYLE.WS_EX_TOOLWINDOW due to config.");
        }
        SystemBackdrop = new TransparentBackdrop();
        root.Background = new SolidColorBrush(Microsoft.UI.Colors.Green);
        root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        #endregion

        // NOTE: This works, but it must be called before any other window commands are invoked e.g. CenterWindow(), Move(), Resize(), et al.
        //var style = Windows.Win32.PInvoke.GetWindowLong(Handle, Windows.Win32.UI.WindowsAndMessaging.WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        //_ = Windows.Win32.PInvoke.SetWindowLong(Handle, Windows.Win32.UI.WindowsAndMessaging.WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)(style & ~(WS_CAPTION | WS_SIZEBOX))); //removes caption and the sizebox from current style

        if (showMessages && Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            this.ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);
        }

        #region [Clock]
        LoadClockfaceAssets();
        tmrClock = new DispatcherTimer();
        tmrClock.Interval = TimeSpan.FromSeconds(1);
        tmrClock.Tick += ClockTimerTick;
        tmrClock.Start();
        #endregion

        #region [Dragging]
        //IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Microsoft.UI.WindowId WndID = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        appW = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(WndID);
        MainGrid.PointerPressed += MainGrid_PointerPressed;
        MainGrid.PointerMoved += MainGrid_PointerMoved;
        MainGrid.PointerReleased += MainGrid_PointerReleased;
        #endregion

        MainGrid.PointerEntered += MainGrid_PointerEntered;
        MainGrid.PointerExited += MainGrid_PointerExited;
        App.OnWindowSizeChanged += AppOnWindowSizeChanged;
    }


    #region [Reactive Opacity]
    void MainGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        clockImage.Opacity = (App.LocalConfig.opacity > 1.0d) ? 1.0d : App.LocalConfig.opacity;
        hourHand.Opacity = (App.LocalConfig.opacity + 0.1d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.1d;
        minuteHand.Opacity = (App.LocalConfig.opacity + 0.1d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.1d;
        secondHand.Opacity = (App.LocalConfig.opacity + 0.1d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.1d;
        radialCenter.Opacity = (App.LocalConfig.opacity + 0.2d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.2d;
    }

    void MainGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        clockImage.Opacity = hourHand.Opacity = minuteHand.Opacity = secondHand.Opacity = radialCenter.Opacity = 1d;
    }
    #endregion

    #region [Window Events]
    void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (App.IsClosing)
            return;

        // Only perform asset updates if window is visible.
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            SetIsAlwaysOnTop(this, true);
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    UpdateClockHands();

                    if (!showMessages)
                        tbInfo.Visibility = CustomTitleBar.Visibility = Visibility.Collapsed;

                    // Initialize dial sizes and image on first activation.
                    if (!initialized)
                    {
                        if (App.LocalConfig.clockFace.EndsWith(".png"))
                            App.LocalConfig.clockFace = App.LocalConfig.clockFace.Replace(".png", "");

                        BitmapImage? img = await LoadImageAtRuntime($"{App.LocalConfig.clockFace}.png");
                        if (img != null)
                            clockImage.Source = img;

                        AppOnWindowSizeChanged(new SizeInt32(App.LocalConfig.windowW, App.LocalConfig.windowH));

                        // Color hands via config values.
                        //hourHand.Stroke = scbHour;
                        //minuteHand.Stroke = scbMinute;
                        //secondHand.Stroke = scbSecond;
                        var length = App.LocalConfig.gradientLength;
                        var darken = App.LocalConfig.gradientDarken;
                        var clr1 = CreateWindowsColor(App.LocalConfig.hourColor);
                        hourHand.Stroke = CreateTipBrush(clr1, darken ? clr1.DarkerBy(0.5F) : clr1.LighterBy(0.5F), length <= 1.0 ? length : 0.5);
                        var clr2 = CreateWindowsColor(App.LocalConfig.minuteColor);
                        minuteHand.Stroke = CreateTipBrush(clr2, darken ? clr2.DarkerBy(0.5F) : clr2.LighterBy(0.5F), length <= 1.0 ? length : 0.5);
                        var clr3 = CreateWindowsColor(App.LocalConfig.secondColor);
                        secondHand.Stroke = CreateTipBrush(clr3, darken ? clr3.DarkerBy(0.5F) : clr3.LighterBy(0.5F), length <= 1.0 ? length : 0.5);

                        initialized = true;
                    }

                    // Layer effect via opacity.
                    clockImage.Opacity = (App.LocalConfig.opacity > 1.0d) ? 1.0d : App.LocalConfig.opacity;
                    hourHand.Opacity = (App.LocalConfig.opacity + 0.1d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.1d;
                    minuteHand.Opacity = (App.LocalConfig.opacity + 0.1d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.1d;
                    secondHand.Opacity = (App.LocalConfig.opacity + 0.1d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.1d;
                    radialCenter.Opacity = (App.LocalConfig.opacity + 0.2d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.2d;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] Clock activation: {ex.Message}");
                }
            });
        }
        else
        {
            Debug.WriteLine($"[NOTICE] Window state is deactivated.");
        }
    }

    void AppOnWindowSizeChanged(Windows.Graphics.SizeInt32 size)
    {
        var total = size.Width + size.Height;
        var ratio = (float)size.Width / (float)size.Height;

        // Ignore invalid aspects.
        if (ratio < 0.8 || ratio > 1.1)
        {
            Debug.WriteLine($"[WARNING] Aspect ratio ({ratio}) is invalid, skipping hand resize.");
            return;
        }

        if (total > 100 && total < 1501)
        {
            Debug.WriteLine($"[INFO] Using non-linear scaling...");
            
            secondHand.Y2 = ScaleNonLinearExp(total, 100, 1500, 24, 250) * -1d;
            secondHand.StrokeThickness = ScaleNonLinearExp(total, 100, 1500, 1, 7);
            
            minuteHand.Y2 = ScaleNonLinearExp(total, 100, 1500, 15, 190) * -1d;
            minuteHand.StrokeThickness = ScaleNonLinearExp(total, 100, 1500, 3, 8);
            
            hourHand.Y2 = ScaleNonLinearExp(total, 100, 1500, 10, 120) * -1d;
            hourHand.StrokeThickness = ScaleNonLinearExp(total, 100, 1500, 4, 9);
        }
        else
        {
            // This scaling should be reworked, it's not exactly linear. These example values are approximations.
            switch (total)
            {
                // Image fidelity will degrade for large sizes.
                case int val when val >= 1500:
                    hourHand.Y2 = -120; minuteHand.Y2 = -220; secondHand.Y2 = -260;
                    hourHand.StrokeThickness = 9; minuteHand.StrokeThickness = 8; secondHand.StrokeThickness = 7;
                    break;
                case int val when val >= 1400:
                    hourHand.Y2 = -110; minuteHand.Y2 = -190; secondHand.Y2 = -240;
                    hourHand.StrokeThickness = 8; minuteHand.StrokeThickness = 7; secondHand.StrokeThickness = 6;
                    break;
                case int val when val >= 1300:
                    hourHand.Y2 = -105; minuteHand.Y2 = -160; secondHand.Y2 = -220;
                    hourHand.StrokeThickness = 8; minuteHand.StrokeThickness = 7; secondHand.StrokeThickness = 6;
                    break;
                case int val when val >= 1200:
                    hourHand.Y2 = -100; minuteHand.Y2 = -140; secondHand.Y2 = -180;
                    hourHand.StrokeThickness = 7; minuteHand.StrokeThickness = 6; secondHand.StrokeThickness = 5;
                    break;
                case int val when val >= 1100:
                    hourHand.Y2 = -90; minuteHand.Y2 = -130; secondHand.Y2 = -170;
                    hourHand.StrokeThickness = 7; minuteHand.StrokeThickness = 6; secondHand.StrokeThickness = 5;
                    break;
                case int val when val >= 1000:
                    hourHand.Y2 = -85; minuteHand.Y2 = -120; secondHand.Y2 = -160;
                    hourHand.StrokeThickness = 7; minuteHand.StrokeThickness = 6; secondHand.StrokeThickness = 5;
                    break;
                case int val when val >= 900:
                    hourHand.Y2 = -80; minuteHand.Y2 = -100; secondHand.Y2 = -130;
                    hourHand.StrokeThickness = 6; minuteHand.StrokeThickness = 5; secondHand.StrokeThickness = 3;
                    break;
                case int val when val >= 800:
                    hourHand.Y2 = -75; minuteHand.Y2 = -90; secondHand.Y2 = -120;
                    hourHand.StrokeThickness = 6; minuteHand.StrokeThickness = 5; secondHand.StrokeThickness = 3;
                    break;
                case int val when val >= 700:
                    hourHand.Y2 = -60; minuteHand.Y2 = -80; secondHand.Y2 = -110;
                    hourHand.StrokeThickness = 5; minuteHand.StrokeThickness = 4; secondHand.StrokeThickness = 2;
                    break;
                case int val when val >= 600:
                    hourHand.Y2 = -50; minuteHand.Y2 = -70; secondHand.Y2 = -90;
                    hourHand.StrokeThickness = 5; minuteHand.StrokeThickness = 4; secondHand.StrokeThickness = 2;
                    break;
                case int val when val >= 500:
                    hourHand.Y2 = -40; minuteHand.Y2 = -59; secondHand.Y2 = -80;
                    hourHand.StrokeThickness = 5; minuteHand.StrokeThickness = 4; secondHand.StrokeThickness = 2;
                    break;
                case int val when val >= 400:
                    hourHand.Y2 = -32; minuteHand.Y2 = -48; secondHand.Y2 = -68;
                    hourHand.StrokeThickness = 5; minuteHand.StrokeThickness = 4; secondHand.StrokeThickness = 2;
                    break;
                case int val when val >= 300:
                    hourHand.Y2 = -22; minuteHand.Y2 = -35; secondHand.Y2 = -45;
                    hourHand.StrokeThickness = 4; minuteHand.StrokeThickness = 3; secondHand.StrokeThickness = 1;
                    break;
                case int val when val >= 200:
                    hourHand.Y2 = -18; minuteHand.Y2 = -27; secondHand.Y2 = -38;
                    hourHand.StrokeThickness = 4; minuteHand.StrokeThickness = 3; secondHand.StrokeThickness = 1;
                    break;
                case int val when val >= 100:
                    hourHand.Y2 = -15; minuteHand.Y2 = -20; secondHand.Y2 = -32;
                    hourHand.StrokeThickness = 4; minuteHand.StrokeThickness = 3; secondHand.StrokeThickness = 1;
                    break;
                default:
                    hourHand.Y2 = -10; minuteHand.Y2 = -15; secondHand.Y2 = -24;
                    hourHand.StrokeThickness = 4; minuteHand.StrokeThickness = 3; secondHand.StrokeThickness = 1;
                    break;
            }
        }
        Debug.WriteLine($"[INFO] Total: {total}, Ratio: {ratio}, Hour.Y2: {hourHand.Y2}, Minute.Y2: {minuteHand.Y2}, Second.Y2: {secondHand.Y2}");
    }

    double ScaleNonLinearExp(double input, double inputMin, double inputMax, double outputMin, double outputMax, double steepness = 1.19)
    {
        input = Math.Max(inputMin, Math.Min(inputMax, input));
        double normalizedInput = (input - inputMin) / (inputMax - inputMin);
        double scaledInput = Math.Pow(normalizedInput, steepness);
        double output = outputMin + (scaledInput * (outputMax - outputMin));
        return output;
    }

    double ScaleNonLinearLog(double input, double inputMin, double inputMax, double outputMin, double outputMax)
    {
        input = Math.Max(inputMin, Math.Min(inputMax, input));
        double logMin = Math.Log(inputMin);
        double logMax = Math.Log(inputMax);
        double logInput = Math.Log(input);
        double scaledLogValue = (logInput - logMin) / (logMax - logMin);
        double output = outputMin + (scaledLogValue * (outputMax - outputMin));
        return output;
    }
    #endregion

    #region [Colors and Brushes]
    static SolidColorBrush CreateSolidColorBrush(string? colorValue)
    {
        if (!string.IsNullOrEmpty(colorValue) && colorValue.Length >= 6)
        {
            var r = colorValue.Substring(0, 2);
            var g = colorValue.Substring(2, 2);
            var b = colorValue.Substring(4, 2);
            return new SolidColorBrush() 
            { 
                Color = Windows.UI.Color.FromArgb(Convert.ToByte("FF", 16), Convert.ToByte(r, 16), Convert.ToByte(g, 16), Convert.ToByte(b, 16)) 
            };
        }
        else // return random color
        {
            return new SolidColorBrush() 
            { 
                Color = Windows.UI.Color.FromArgb(Convert.ToByte("FF", 16), Convert.ToByte(Random.Shared.Next(0, 256)), Convert.ToByte(Random.Shared.Next(0, 256)), Convert.ToByte(Random.Shared.Next(0, 256))) 
            };
        }
    }

    static Windows.UI.Color CreateWindowsColor(string? colorValue)
    {
        if (!string.IsNullOrEmpty(colorValue) && colorValue.Length >= 6)
        {
            var r = colorValue.Substring(0, 2);
            var g = colorValue.Substring(2, 2);
            var b = colorValue.Substring(4, 2);
            return Windows.UI.Color.FromArgb(Convert.ToByte("FF", 16), Convert.ToByte(r, 16), Convert.ToByte(g, 16), Convert.ToByte(b, 16));
        }
        else // return random color
        {
            return Windows.UI.Color.FromArgb(Convert.ToByte("FF", 16), Convert.ToByte(Random.Shared.Next(0, 256)), Convert.ToByte(Random.Shared.Next(0, 256)), Convert.ToByte(Random.Shared.Next(0, 256)));
        }
    }

    /// <summary>
    /// Creates a <see cref="LinearGradientBrush"/> from 2 input colors.
    /// </summary>
    /// <param name="c1">offset 0.9 color</param>
    /// <param name="c2">offset 1.0 color</param>
    /// <returns><see cref="LinearGradientBrush"/></returns>
    static LinearGradientBrush CreateTipBrush(Windows.UI.Color c1, Windows.UI.Color c2, double length = 0.85)
    {
        var gs1 = new GradientStop(); gs1.Color = c1; gs1.Offset = length;
        var gs2 = new GradientStop(); gs2.Color = c2; gs2.Offset = 1.0;
        var gsc = new GradientStopCollection() { gs1, gs2 };
        var lgb = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 1),
            EndPoint = new Windows.Foundation.Point(0, 0),
            GradientStops = gsc
        };
        return lgb;
    }

    /// <summary>
    /// Creates a <see cref="LinearGradientBrush"/> from 1 input color that has a shadow edge at the bottom.
    /// </summary>
    /// <returns><see cref="LinearGradientBrush"/></returns>
    static LinearGradientBrush CreateShadowBrush(Windows.UI.Color c1)
    {
        var gs1 = new GradientStop(); gs1.Color = c1; gs1.Offset = 0.7;
        var gs2 = new GradientStop(); gs2.Color = Microsoft.UI.Colors.Black; gs2.Offset = 1.0;
        var gsc = new GradientStopCollection() { gs1, gs2 };
        var lgb = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(1, 0),
            EndPoint = new Windows.Foundation.Point(0, 0),
            GradientStops = gsc
        };
        return lgb;
    }
    #endregion

    #region [Clockface]
    void ClockTimerTick(object? sender, object e)
    {
        if (App.IsClosing)
            return;

        UpdateClockHands();
        if (!showMessages)
            this.Title = DateTime.Now.ToString("hh:mm tt");
    }

    void UpdateClockHands()
    {
        DateTime current = DateTime.Now;
        double hourAngle = (current.Hour % 12d + current.Minute / 60d) * 30d; // 30 degrees per hour
        double minuteAngle = current.Minute * 6d; // 6 degrees per minute
        double secondAngle = current.Second * 6d; // 6 degrees per second
        if (useRoundedHands)
        {
            hourHand.Visibility = minuteHand.Visibility = secondHand.Visibility = Visibility.Collapsed;
            // Since Microsoft.UI.Xaml.Shapes.Rectangle cannot have negative
            // values, we'll need to set the angle 180° out of phase.
            double oppositeAngle = (hourAngle + 180) % 360;
            hourHand2.RenderTransform = new RotateTransform { Angle = oppositeAngle, CenterX = 1, CenterY = 1 };
            oppositeAngle = (minuteAngle + 180) % 360;
            minuteHand2.RenderTransform = new RotateTransform { Angle = oppositeAngle, CenterX = 1, CenterY = 1 };
            oppositeAngle = (secondAngle + 180) % 360;
            secondHand2.RenderTransform = new RotateTransform { Angle = oppositeAngle, CenterX = 1, CenterY = 1 };
        }
        else
        {
            hourHand2.Visibility = minuteHand2.Visibility = secondHand2.Visibility = Visibility.Collapsed;
            hourHand.RenderTransform = new RotateTransform { Angle = hourAngle, CenterX = 0, CenterY = 0 };
            minuteHand.RenderTransform = new RotateTransform { Angle = minuteAngle, CenterX = 0, CenterY = 0 };
            secondHand.RenderTransform = new RotateTransform { Angle = secondAngle, CenterX = 0, CenterY = 0 };
        }

        // Update tooltip once per minute.
        if (current.Second % 60 == 0)
            ToolTipService.SetToolTip(MainGrid, DateTime.Now.ToString("hh:mm tt"));
    }

    /// <summary>
    /// Load our image roster.
    /// </summary>
    void LoadClockfaceAssets()
    {
        string path = string.Empty;
        if (!App.IsPackaged)
            path = Path.Combine(Directory.GetCurrentDirectory(), "Assets");
        else
            path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Assets");

        //foreach (var f in Directory.GetFiles(path, "Clock*.png", SearchOption.TopDirectoryOnly))
        //{
        //    clockAssets.Add(Path.GetFileName(f));
        //}

        DirectoryInfo? searchDI = new DirectoryInfo(path);
        FileInfo[]? files = searchDI?.GetFiles("Clock*.png", SearchOption.TopDirectoryOnly);
        if (files != null)
        {
            IOrderedEnumerable<FileInfo>? sorted = files.OrderByDescending(f => f.LastWriteTime);
            foreach (var file in sorted) 
            {
                clockAssets.Add(file.Name);
            }
        }

        if (App.LocalConfig.randomHands)
        {
            Random.Shared.NextBytes(rndBytes);
            scbHour = new SolidColorBrush() { Color = Color.FromArgb(Convert.ToByte("FF", 16), rndBytes[0], rndBytes[1], rndBytes[2]) };
            Random.Shared.NextBytes(rndBytes);
            scbMinute = new SolidColorBrush() { Color = Color.FromArgb(Convert.ToByte("FF", 16), rndBytes[0], rndBytes[1], rndBytes[2]) };
            Random.Shared.NextBytes(rndBytes);
            scbSecond = new SolidColorBrush() { Color = Color.FromArgb(Convert.ToByte("FF", 16), rndBytes[0], rndBytes[1], rndBytes[2]) };
        }
        else
        {
            scbHour = CreateSolidColorBrush(App.LocalConfig.hourColor);
            scbMinute = CreateSolidColorBrush(App.LocalConfig.minuteColor);
            scbSecond = CreateSolidColorBrush(App.LocalConfig.secondColor);
        }
    }
    #endregion

    #region [Drag Events]
    void MainGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Debug.WriteLine($"[INFO] {((Grid)sender).Name} PointerPressed");
        ((UIElement)sender).CapturePointer(e.Pointer);
        var currentPoint = e.GetCurrentPoint((UIElement)sender);
        if (showMessages)
            tbInfo.Text = $"Pointer pressed at {currentPoint.Position}";
        if (currentPoint.Properties.IsLeftButtonPressed)
        {
            ((UIElement)sender).CapturePointer(e.Pointer);
            windowStartX = appW.Position.X;
            windowStartY = appW.Position.Y;
            Windows.Graphics.PointInt32 pt;
            GetCursorPos(out pt); // user32.dll
            initialPointerX = pt.X;
            initialPointerY = pt.Y;
            isMoving = true;
        }
        else if (currentPoint.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            if (clockAssets.Count > 0)
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        if (assetIndex >= clockAssets.Count)
                            assetIndex = 0;
                        App.LocalConfig.clockFace = clockAssets[assetIndex];
                        assetIndex++;
                        Debug.WriteLine($"[INFO] {App.LocalConfig.clockFace}");
                        BitmapImage? img = await LoadImageAtRuntime($"{App.LocalConfig.clockFace}");
                        if (img != null)
                            clockImage.Source = img;
                    }
                    catch (Exception) { }
                });
            }
        }
        else if (currentPoint.Properties.IsMiddleButtonPressed)
        {
            e.Handled = true;
            Application.Current.Exit();
        }
    }

    void MainGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        Debug.WriteLine($"[INFO] {((Grid)sender).Name} PointerReleased");
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        if (showMessages)
            tbInfo.Text = $"Pointer released";
        isMoving = false;
    }

    void MainGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var currentPoint = e.GetCurrentPoint((UIElement)sender);
        if (showMessages)
            tbInfo.Text = $"Pointer moved to {currentPoint.Position.X.ToString().PadLeft(3,'0')},{currentPoint.Position.Y.ToString().PadLeft(3, '0')}";
        if (currentPoint.Properties.IsLeftButtonPressed)
        {
            Windows.Graphics.PointInt32 pt;
            GetCursorPos(out pt);
            if (isMoving)
                appW.Move(new Windows.Graphics.PointInt32(windowStartX + (pt.X - initialPointerX), windowStartY + (pt.Y - initialPointerY)));
        }
    }
    #endregion

    #region [AlwaysOnTop Helpers]
    /// <summary>
    /// Configures whether the window should always be displayed on top of other windows or not
    /// </summary>
    /// <remarks>The presenter must be an overlapped presenter.</remarks>
    /// <exception cref="NotSupportedException">Throw if the AppWindow Presenter isn't an overlapped presenter.</exception>
    /// <param name="window"><see cref="Microsoft.UI.Xaml.Window"/></param>
    /// <param name="enable">true to set always on top, false otherwise</param>
    void SetIsAlwaysOnTop(Microsoft.UI.Xaml.Window window, bool enable) => UpdateOverlappedPresenter(window, (op) => op.IsAlwaysOnTop = enable);
    void UpdateOverlappedPresenter(Microsoft.UI.Xaml.Window window, Action<Microsoft.UI.Windowing.OverlappedPresenter> action)
    {
        if (window is null)
            throw new ArgumentNullException(nameof(window));

        var appwindow = GetAppWindow(window);

        if (appwindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
            action(overlapped);
        else
            throw new NotSupportedException($"Not supported with a {appwindow.Presenter.Kind} presenter.");
    }

    /// <summary>
    /// Gets the <see cref="Microsoft.UI.Windowing.AppWindow"/> for the window.
    /// </summary>
    /// <param name="window"><see cref="Microsoft.UI.Xaml.Window"/></param>
    /// <returns><see cref="Microsoft.UI.Windowing.AppWindow"/></returns>
    Microsoft.UI.Windowing.AppWindow GetAppWindow(Microsoft.UI.Xaml.Window window) => GetAppWindowFromWindowHandle(WindowNative.GetWindowHandle(window));

    /// <summary>
    /// Gets the <see cref="Microsoft.UI.Windowing.AppWindow"/> from an HWND.
    /// </summary>
    /// <param name="hwnd"><see cref="IntPtr"/> of the window</param>
    /// <returns><see cref="Microsoft.UI.Windowing.AppWindow"/></returns>
    Microsoft.UI.Windowing.AppWindow GetAppWindowFromWindowHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            throw new ArgumentNullException(nameof(hwnd));

        Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);

        return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
    }
    #endregion

    #region [Image Helpers]
    async Task<BitmapImage> LoadImageAtRuntime(string imageName)
    {
        try
        {
            BitmapImage bitmapImage = new BitmapImage();
            var uri = new Uri($"ms-appx:///Assets/{imageName}");
#if IS_UNPACKAGED
            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.Combine(Directory.GetCurrentDirectory(), $"Assets\\{imageName}"));
#else
            StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(uri);
#endif
            using (var stream = await file.OpenAsync(FileAccessMode.Read))
            {
                await bitmapImage.SetSourceAsync(stream); // Set the BitmapImage source to the stream.
            }
            return bitmapImage;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] LoadImageAtRuntime: {ex.Message}");
            return new BitmapImage() { UriSource = new Uri($"ms-appx:///Assets/{imageName}") };
        }
    }


    /// <summary>
    /// Generic loader for software bitmaps.
    /// </summary>
    /// <param name="filePath">Full path to asset.</param>
    /// <returns><see cref="Windows.Graphics.Imaging.SoftwareBitmap"/></returns>
    async Task<Windows.Graphics.Imaging.SoftwareBitmap?> LoadSoftwareBitmap(string filePath)
    {
        try
        {
            Windows.Graphics.Imaging.SoftwareBitmap? softwareBitmap;
            Windows.Storage.StorageFile inputFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
            using (Windows.Storage.Streams.IRandomAccessStream ras = await inputFile.OpenAsync(Windows.Storage.FileAccessMode.Read))
            {
                // Create the decoder from the stream
                Windows.Graphics.Imaging.BitmapDecoder decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
                // Get the SoftwareBitmap representation of the file
                softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            }
            return softwareBitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] LoadSoftwareBitmap: {ex.Message}");
            return null;
        }
    }

    async Task<Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap?> OpenWriteableBitmap(Windows.Storage.StorageFile storageFile)
    {
        try
        {
            using (Windows.Storage.Streams.IRandomAccessStream stream = await storageFile.OpenAsync(Windows.Storage.FileAccessMode.Read))
            {
                Windows.Graphics.Imaging.BitmapDecoder decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap image = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap((int)decoder.PixelWidth, (int)decoder.PixelHeight);
                image.SetSource(stream);
                return image;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] OpenWriteableBitmap: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns an encoder <see cref="Guid"/> based on the <paramref name="fileName"/> extension.
    /// </summary>
    Guid GetEncoderId(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName);
        if (new[] { ".bmp", ".dib" }.Contains(ext))
            return Windows.Graphics.Imaging.BitmapEncoder.BmpEncoderId;
        else if (new[] { ".tiff", ".tif" }.Contains(ext))
            return Windows.Graphics.Imaging.BitmapEncoder.TiffEncoderId;
        else if (new[] { ".gif" }.Contains(ext))
            return Windows.Graphics.Imaging.BitmapEncoder.GifEncoderId;
        else if (new[] { ".jpg", ".jpeg", ".jpe", ".jfif", ".jif" }.Contains(ext))
            return Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId;
        else if (new[] { ".hdp", ".jxr", ".wdp" }.Contains(ext))
            return Windows.Graphics.Imaging.BitmapEncoder.JpegXREncoderId;
        else if (new[] { ".heic", ".heif", ".heifs" }.Contains(ext))
            return Windows.Graphics.Imaging.BitmapEncoder.HeifEncoderId;
        else // default will be PNG
            return Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId;
    }
    #endregion
}
