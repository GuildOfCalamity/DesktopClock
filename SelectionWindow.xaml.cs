using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using WinRT; // required to support Window.As<ICompositionSupportsSystemBackdrop>()

namespace Draggable;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SelectionWindow : Window
{
    bool thisIsClosing = false;
    SystemBackdropConfiguration ? _configurationSource;
    DesktopAcrylicController? _acrylicController;
    Microsoft.UI.Windowing.AppWindow? appWindow;
    public ObservableCollection<AssetIndexItem> ClockItems { get; set; } = new();
    public Action<AssetIndexItem?> ClockSelectedEvent = delegate { };

    AssetIndexItem? _selectedClock;
    public AssetIndexItem? SelectedClock
    {
        get { return _selectedClock; }
        set { _selectedClock = value; }
    }

    public SelectionWindow()
    {
        this.InitializeComponent();

        this.ExtendsContentIntoTitleBar = true;
        this.Title = "Clock Assets";
        SetTitleBar(CustomTitleBar);
        this.Activated += SelectionWindow_Activated;
        this.Closed += SelectionWindow_Closed;
        AssetsRepeater.Loaded += AssetsRepeaterOnLoaded;

        #region [AppWindow and Icon]
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this); // Retrieve the window handle (HWND) of the current (XAML) WinUI3 window.
        Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd); // Retrieve the WindowId that corresponds to hWnd.
        appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId); // Lastly, retrieve the AppWindow for the current (XAML) WinUI3 window.
        if (appWindow is not null)
        {
            appWindow.Closing += (s, e) => { thisIsClosing = true; };

            if (App.IsPackaged)
                appWindow?.SetIcon(System.IO.Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, $"Assets/StoreLogo.ico"));
            else
                appWindow?.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, $"Assets/StoreLogo.ico"));
        }
        #endregion

        // https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/system-backdrop-controller
        if (DesktopAcrylicController.IsSupported())
        {
            // Hook up the policy object.
            _configurationSource = new SystemBackdropConfiguration();
            // Create the desktop controller.
            _acrylicController = new Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController();
            _acrylicController.TintOpacity = 0.4f; // Lower value may be too translucent vs light background.
            _acrylicController.LuminosityOpacity = 0.1f;
            _acrylicController.TintColor = Microsoft.UI.Colors.Gray;
            // Fall-back color is only used when the window state becomes deactivated.
            _acrylicController.FallbackColor = Microsoft.UI.Colors.Transparent;
            // Note: Be sure to have "using WinRT;" to support the Window.As<T>() call.
            _acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
            _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
        }
        else
        {
            root.Background = (SolidColorBrush)App.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        }

    }

    #region [Window Events]
    void SelectionWindow_Closed(object sender, WindowEventArgs args)
    {
        // Make sure the Acrylic controller is disposed
        // so it doesn't try to access a closed window.
        if (_acrylicController is not null)
        {
            _acrylicController.Dispose();
            _acrylicController = null;
        }
    }

    void SelectionWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        //if (args.WindowActivationState != WindowActivationState.Deactivated)
        //{
        //    appWindow?.Resize(new Windows.Graphics.SizeInt32(780, 480));
        //    App.CenterWindow(this);
        //}
    }
    #endregion

    /// <summary>
    /// We can change this later to use a shared asset pool when the app first loads.
    /// I'm leaving this where it will refresh each time in the event that the user 
    /// adds new assets while the application is still running.
    /// </summary>
    void AssetsRepeaterOnLoaded(object sender, RoutedEventArgs e)
    {
        if (ClockItems.Count > 0 || thisIsClosing)
            return;

        appWindow?.Resize(new Windows.Graphics.SizeInt32(780, 480));
        App.CenterWindow(this);

        // Delegate loading of clocks, so we have smooth navigating to
        // this page and do not unnecessarily block the UI thread.
        // On startup there won't be anything in the collection, but
        // in the event that you decide to load a large number of
        // items from disk, this will facilitate that process.
        Task.Run(delegate ()
        {
            string path = string.Empty;
            if (!App.IsPackaged)
                path = Path.Combine(Directory.GetCurrentDirectory(), "Assets");
            else
                path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Assets");

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
            {
                if (thisIsClosing)
                    return;

                DirectoryInfo? searchDI = new DirectoryInfo(path);
                FileInfo[]? files = searchDI?.GetFiles("Clock*.png", SearchOption.TopDirectoryOnly);
                if (files != null)
                {
                    IOrderedEnumerable<FileInfo>? sorted = files.OrderByDescending(f => f.LastWriteTime);
                    foreach (var file in sorted)
                    {
                        if (thisIsClosing)
                            break;

                        BitmapImage? img = await Extensions.LoadImageAtRuntime($"{file.Name}");
                        ClockItems.Add(new AssetIndexItem { ClockName = $"{System.IO.Path.GetFileNameWithoutExtension(file.Name)}", ClockImage = img });
                    }
                }
            });

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                if (thisIsClosing)
                    return;

                AssetsRepeater.ItemsSource = ClockItems;
            });
        });
    }

    void IconsTemplateOnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        int oldIndex = 0;
        
        if (SelectedClock is not null)
            oldIndex = ClockItems.IndexOf(SelectedClock);

        var previousItem = AssetsRepeater.TryGetElement(oldIndex);
        if (previousItem is not null)
            MoveToSelectionState(previousItem, false);

        var obj = sender as UIElement;
        if (obj is not null)
        {
            var itemIndex = AssetsRepeater.GetElementIndex(obj);
            if (itemIndex != -1)
            {
                SelectedClock = ClockItems[itemIndex != -1 ? itemIndex : 0];
                Debug.WriteLine($"[INFO] Moving to selection index {itemIndex}.");
                MoveToSelectionState(obj, true);
                ClockSelectedEvent.Invoke(SelectedClock);
            }
            else
            {
                Debug.WriteLine($"[WARNING] GetElementIndex was not valid.");
            }
        }
    }

    void IconsTemplateOnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        var obj = sender as UIElement;
        if (obj is not null)
        {
            var itemIndex = AssetsRepeater.GetElementIndex(obj);
            if (itemIndex != -1)
            {
                SelectedClock = ClockItems[itemIndex];
                Debug.WriteLine($"[INFO] De-selecting index {itemIndex}.");
                MoveToSelectionState(obj, true);
            }
            else
            {
                Debug.WriteLine($"[WARNING] GetElementIndex was not valid.");
            }
        }
    }

    void IconsTemplateOnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var obj = sender as UIElement;
        if (obj is not null)
        {
            var itemIndex = AssetsRepeater.GetElementIndex(obj);
            if (itemIndex != -1)
            {
                SelectedClock = ClockItems[itemIndex];
                Debug.WriteLine($"[INFO] De-selecting index {itemIndex}.");
                MoveToSelectionState(obj, false);
            }
            else
            {
                Debug.WriteLine($"[WARNING] GetElementIndex was not valid.");
            }
        }
    }

    void ClearAllSelections()
    {
        if (ClockItems.Count == 0) 
            return;

        for (int i = 0; i < ClockItems.Count; i++)
        {
            if (thisIsClosing)
                break;

            var item = AssetsRepeater.TryGetElement(i);
            if (item is not null)
                MoveToSelectionState(item, false);
        }
    }

    /// <summary>
    /// Activate the proper VisualStateGroup for the control.
    /// </summary>
    static void MoveToSelectionState(UIElement previousItem, bool isSelected)
    {
        try { VisualStateManager.GoToState(previousItem as Control, isSelected ? "Selected" : "Default", false); }
        catch (NullReferenceException ex) { App.DebugLog($"[{previousItem.NameOf()}] {ex.Message}"); }
    }

}

public class AssetIndexItem
{
    public string? ClockName { get; set; }
    public BitmapImage? ClockImage { get; set; }
}

