// ********************************************************************************************************************
//
// MainWindow.xaml.cs -- ui c# for main window
//
// Copyright(C) 2025 ilominar/raven
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
// implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
// for more details.
//
// You should have received a copy of the GNU General Public License along with this program.  If not, see
// <https://www.gnu.org/licenses/>.
//
// ********************************************************************************************************************

using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ViperOps2MIZ.UI;
using Windows.Foundation;
using Windows.Graphics;

namespace ViperOps2MIZ
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        // ------------------------------------------------------------------------------------------------------------
        //
        // windoze interfaces & data structs
        //
        // ------------------------------------------------------------------------------------------------------------

        private delegate IntPtr WinProc(IntPtr hWnd, WindowMessage Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("User32.dll")]
        internal static extern int GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, WindowLongIndexFlags nIndex, WinProc newProc);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, WindowLongIndexFlags nIndex, WinProc newProc);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, WindowMessage Msg, IntPtr wParam, IntPtr lParam);

        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [Flags]
        private enum WindowLongIndexFlags : int
        {
            GWL_WNDPROC = -4,
        }

        private enum WindowMessage : int
        {
            WM_GETMINMAXINFO = 0x0024,
        }

        // ------------------------------------------------------------------------------------------------------------
        //
        // properties
        //
        // ------------------------------------------------------------------------------------------------------------

        public MainInterfacePage MainInterfacePageObj { get; private set; }

        // ---- internal properties

        private static WinProc? _newWndProc = null;
        private static IntPtr _oldWndProc = IntPtr.Zero;

        private static int _baseWindowWidth = 575;
        private static int _baseWindowHeight = 314;

        private static int _minWindowWidth = 500;
        private static int _maxWindowWidth = 700;
        private static int _minWindowHeight = 314;
        private static int _maxWindowHeight = 500;

        // ------------------------------------------------------------------------------------------------------------
        //
        // construction
        //
        // ------------------------------------------------------------------------------------------------------------


        public MainWindow()
        {
            InitializeComponent();

            Title = "KML2MIZ";

            SystemBackdrop = new DesktopAcrylicBackdrop();
            // SystemBackdrop = new MicaBackdrop() { Kind = MicaKind.BaseAlt };
            // uiAppTitleBar.Loaded += AppTitleBar_Loaded;
            // uiAppTitleBar.SizeChanged += AppTitleBar_SizeChanged;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(uiAppTitleBar);

            var hWnd = GetWindowHandleForCurrentWindow(this);

            var scalingFactor = (float)GetDpiForWindow(hWnd) / 96;
            SizeInt32 baseSize;
            baseSize.Height = (int)(_baseWindowHeight * scalingFactor);
            baseSize.Width = (int)(_baseWindowWidth * scalingFactor);
            AppWindow.Resize(baseSize);

            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow != null)
            {
                DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
                if (displayArea != null)
                {
                    // TODO: track last window position and move to there rather than always centering?
                    var CenteredPosition = appWindow.Position;
                    CenteredPosition.X = ((displayArea.WorkArea.Width - appWindow.Size.Width) / 2);
                    CenteredPosition.Y = ((displayArea.WorkArea.Height - appWindow.Size.Height) / 2);
                    appWindow.Move(CenteredPosition);

                    _maxWindowWidth = Math.Max(_maxWindowWidth, displayArea.WorkArea.Width);
                    _maxWindowHeight = Math.Max(_maxWindowHeight, displayArea.WorkArea.Height);
                }
                appWindow.SetIcon(@"Images/JAFDTC_Icon.ico");
            }

            // sets up min/max window sizes using the right magic. code pulled from stackoverflow:
            //
            // https://stackoverflow.com/questions/72825683/wm-getminmaxinfo-in-winui-3-with-c
            //
            _newWndProc = new WinProc(WndProc);
            _oldWndProc = SetWindowLongPtr(hWnd, WindowLongIndexFlags.GWL_WNDPROC, _newWndProc);

            uiAppContentFrame.Navigate(typeof(MainInterfacePage), null);
            MainInterfacePageObj = (MainInterfacePage)uiAppContentFrame.Content;
        }

        // ------------------------------------------------------------------------------------------------------------
        //
        // window sizing support
        //
        // ------------------------------------------------------------------------------------------------------------

        // sets up min/max window sizes using the right magic, see
        //
        // https://stackoverflow.com/questions/72825683/wm-getminmaxinfo-in-winui-3-with-c

        private static IntPtr GetWindowHandleForCurrentWindow(object target) =>
            WinRT.Interop.WindowNative.GetWindowHandle(target);

        private static IntPtr WndProc(IntPtr hWnd, WindowMessage Msg, IntPtr wParam, IntPtr lParam)
        {
            switch (Msg)
            {
                case WindowMessage.WM_GETMINMAXINFO:
                    var dpi = GetDpiForWindow(hWnd);
                    var scalingFactor = (float)dpi / 96;

                    var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    minMaxInfo.ptMinTrackSize.x = (int)(_minWindowWidth * scalingFactor);
                    minMaxInfo.ptMaxTrackSize.x = (int)(_maxWindowWidth * scalingFactor);
                    minMaxInfo.ptMinTrackSize.y = (int)(_minWindowHeight * scalingFactor);
                    minMaxInfo.ptMaxTrackSize.y = (int)(_maxWindowHeight * scalingFactor);

                    Marshal.StructureToPtr(minMaxInfo, lParam, true);
                    break;

            }
            return CallWindowProc(_oldWndProc, hWnd, Msg, wParam, lParam);
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, WindowLongIndexFlags nIndex, WinProc newProc)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, newProc);
            else
                return new IntPtr(SetWindowLong32(hWnd, nIndex, newProc));
        }

        // ------------------------------------------------------------------------------------------------------------
        //
        // startup support
        //
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// content frame loaded.
        /// </summary>
        private void AppContentFrame_Loaded(object sender, RoutedEventArgs args)
        {
        }
    }
}
