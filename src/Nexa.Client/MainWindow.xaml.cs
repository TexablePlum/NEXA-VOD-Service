using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nexa.Client.Views;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace Nexa.Client
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Rozszerza treść na pasek tytułu
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // Ustaw minimalny rozmiar okna (1220x700)
            SetMinimumWindowSize(1220, 700);

            // Startuje od Splash Screena
            AppFrame.Navigate(typeof(SplashPage));
        }

        private void SetMinimumWindowSize(int minWidth, int minHeight)
        {
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                // W WinUI 3 nie ma bezpośredniego API dla MinSize
                // Ale możemy ograniczyć resize poprzez event handler
                appWindow.Changed += (sender, args) =>
                {
                    if (args.DidSizeChange)
                    {
                        var size = appWindow.Size;
                        if (size.Width < minWidth || size.Height < minHeight)
                        {
                            appWindow.Resize(new SizeInt32
                            {
                                Width = Math.Max(size.Width, minWidth),
                                Height = Math.Max(size.Height, minHeight)
                            });
                        }
                    }
                };
            }
        }
    }
}
