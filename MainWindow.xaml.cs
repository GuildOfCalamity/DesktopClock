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
using Windows.Storage;
using WinRT.Interop;


namespace Draggable;

public sealed partial class MainWindow : Window
{
    int assetIndex = 0;
    bool showMessages = false;
    List<string> clockAssets = new();

    #region [Transparency]
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

    DispatcherTimer tmrClock;

    public MainWindow()
    {
        this.InitializeComponent();
        this.Activated += MainWindow_Activated;
        this.Title = $"{App.GetCurrentAssemblyName()}";

        #region [Transparency]
        var hwnd = WindowNative.GetWindowHandle(this);
        Handle = new Windows.Win32.Foundation.HWND(hwnd);
        WinExStyle |= Windows.Win32.UI.WindowsAndMessaging.WINDOW_EX_STYLE.WS_EX_LAYERED; // We'll use WS_EX_LAYERED, not WS_EX_TRANSPARENT, for the effect.
        SystemBackdrop = new TransparentBackdrop();
        root.Background = new SolidColorBrush(Microsoft.UI.Colors.Green);
        root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        #endregion

        if (showMessages && Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            this.ExtendsContentIntoTitleBar = true;
            SetTitleBar(CustomTitleBar);
        }

        LoadClockfaceAssets();

        #region [Clock]
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
    }

    void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (App.IsClosing)
            return;

        if (!showMessages)
            tbInfo.Visibility = CustomTitleBar.Visibility = Visibility.Collapsed;

        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            UpdateClockFace();
            SetIsAlwaysOnTop(this, true);
        }

        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (App.LocalConfig.clockFace.EndsWith(".png"))
                    App.LocalConfig.clockFace = App.LocalConfig.clockFace.Replace(".png", "");
                BitmapImage? img = await LoadImageAtRuntime($"{App.LocalConfig.clockFace}.png");
                if (img != null)
                    clockImage.Source = img;

                // Layer effect via opacity.
                clockImage.Opacity = App.LocalConfig.opacity > 1.0d ? 1.0d : App.LocalConfig.opacity;
                hourHand.Opacity = (App.LocalConfig.opacity + 0.15d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.15d;
                minuteHand.Opacity = (App.LocalConfig.opacity + 0.15d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.15d;
                secondHand.Opacity = (App.LocalConfig.opacity + 0.15d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.15d;
                outterCenter.Opacity = (App.LocalConfig.opacity + 0.25d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.25d;
                innerCenter.Opacity = (App.LocalConfig.opacity + 0.25d > 1.0d) ? 1.0d : App.LocalConfig.opacity + 0.25d;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Clock setup: {ex.Message}");
            }
        });
    }

    #region [Clockface]
    void ClockTimerTick(object? sender, object e)
    {
        if (App.IsClosing)
            return;

        UpdateClockFace();
        if (!showMessages)
            this.Title = DateTime.Now.ToString("hh:mm tt");
    }

    void UpdateClockFace()
    {
        DateTime current = DateTime.Now;
        double hourAngle = (current.Hour % 12d + current.Minute / 60d) * 30d; // 30 degrees per hour
        double minuteAngle = current.Minute * 6d; // 6 degrees per minute
        double secondAngle = current.Second * 6d; // 6 degrees per second
        hourHand.RenderTransform = new RotateTransform { Angle = hourAngle, CenterX = 0, CenterY = 0 };
        minuteHand.RenderTransform = new RotateTransform { Angle = minuteAngle, CenterX = 0, CenterY = 0 };
        secondHand.RenderTransform = new RotateTransform { Angle = secondAngle, CenterX = 0, CenterY = 0 };
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

        foreach (var f in Directory.GetFiles(path, "Clock*.png", SearchOption.TopDirectoryOnly))
        {
            clockAssets.Add(Path.GetFileName(f));
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
            //Application.Current.Exit();
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
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ERROR] {ex.Message}");
                    }
                });
            }
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
