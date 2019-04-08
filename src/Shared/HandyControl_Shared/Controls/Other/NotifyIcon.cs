﻿using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HandyControl.Data;
using HandyControl.Tools;
using HandyControl.Tools.Interop;

namespace HandyControl.Controls
{
    public class NotifyIcon : FrameworkElement, IDisposable
    {
        private bool _added;

        private readonly object _syncObj = new object();

        private readonly int _id;

        private static int NextId;

        private ImageSource _icon;

        private IntPtr _iconCurrentHandle;

        private IntPtr _iconDefaultHandle;

        private IconHandle _iconHandle;

        private const int WmTrayMouseMessage = NativeMethods.WM_USER + 1024;

        private string _windowClassName;

        private int _wmTaskbarCreated;

        private IntPtr _messageWindowHandle;

        private readonly WndProc _callback;

        private Popup _contextContent;

        private bool _doubleClick;

        private DispatcherTimer _dispatcherTimer;

        private bool _isTransparent;

        private bool _isDisposed;

        static NotifyIcon()
        {
            VisibilityProperty.OverrideMetadata(typeof(NotifyIcon), new PropertyMetadata(Visibility.Visible, OnVisibilityChanged));
        }

        private static void OnVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (NotifyIcon) d;
            var v = (Visibility)e.NewValue;

            if (v == Visibility.Visible)
            {
                if (ctl._iconCurrentHandle == IntPtr.Zero)
                {
                    ctl.OnIconChanged();
                }
                ctl.UpdateIcon(true);
            }
            else if(ctl._iconCurrentHandle != IntPtr.Zero)
            {
                ctl.UpdateIcon(false);
            }
        }

        public NotifyIcon()
        {
            _id = ++NextId;
            _callback = Callback;

            Loaded += (s, e) =>
            {
                RegisterClass();
                if (Visibility == Visibility.Visible)
                {
                    OnIconChanged();
                    UpdateIcon(true);
                }
            };

            if (Application.Current != null) Application.Current.Exit += (s, e) => Dispose();
        }

        ~NotifyIcon()
        {
            Dispose(false);
        }

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            "Text", typeof(string), typeof(NotifyIcon), new PropertyMetadata(default(string)));

        public string Text
        {
            get => (string) GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
            "Icon", typeof(ImageSource), typeof(NotifyIcon), new PropertyMetadata(default(ImageSource), OnIconChanged));

        private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (NotifyIcon)d;
            ctl._icon = (ImageSource)e.NewValue;
            ctl.OnIconChanged();
        }

        public ImageSource Icon
        {
            get => (ImageSource)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly DependencyProperty ContextContentProperty = DependencyProperty.Register(
            "ContextContent", typeof(object), typeof(NotifyIcon), new PropertyMetadata(default(object)));

        public object ContextContent
        {
            get => GetValue(ContextContentProperty);
            set => SetValue(ContextContentProperty, value);
        }

        public static readonly DependencyProperty BlinkIntervalProperty = DependencyProperty.Register(
            "BlinkInterval", typeof(TimeSpan), typeof(NotifyIcon), new PropertyMetadata(TimeSpan.FromMilliseconds(500), OnBlinkIntervalChanged));

        private static void OnBlinkIntervalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (NotifyIcon)d;
            if (ctl._dispatcherTimer != null)
            {
                ctl._dispatcherTimer.Interval = (TimeSpan) e.NewValue;
            }
        }

        public TimeSpan BlinkInterval
        {
            get => (TimeSpan) GetValue(BlinkIntervalProperty);
            set => SetValue(BlinkIntervalProperty, value);
        }

        public static readonly DependencyProperty IsBlinkProperty = DependencyProperty.Register(
            "IsBlink", typeof(bool), typeof(NotifyIcon), new PropertyMetadata(ValueBoxes.FalseBox, OnIsBlinkChanged));

        private static void OnIsBlinkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (NotifyIcon)d;
            if (ctl.Visibility != Visibility.Visible) return;
            if ((bool) e.NewValue)
            {
                if (ctl._dispatcherTimer == null)
                {
                    ctl._dispatcherTimer = new DispatcherTimer
                    {
                        Interval = ctl.BlinkInterval
                    };
                    ctl._dispatcherTimer.Tick += ctl.DispatcherTimer_Tick;
                }
                ctl._dispatcherTimer.Start();
            }
            else
            {
                ctl._dispatcherTimer?.Stop();
                ctl._dispatcherTimer = null;
                ctl.UpdateIcon(true);
            }
        }

        private void DispatcherTimer_Tick(object sender, EventArgs e)
        {
            if (Visibility != Visibility.Visible || _iconCurrentHandle == IntPtr.Zero) return;
            UpdateIcon(true, !_isTransparent);
        }

        public bool IsBlink
        {
            get => (bool) GetValue(IsBlinkProperty);
            set => SetValue(IsBlinkProperty, value);
        }

        private void OnIconChanged()
        {
            if (_icon != null)
            {
                IconHelper.GetIconHandlesFromImageSource(_icon, out _, out _iconHandle);
                _iconCurrentHandle = _iconHandle.CriticalGetHandle();
            }
            else
            {
                if (_iconDefaultHandle == IntPtr.Zero)
                {
                    IconHelper.GetDefaultIconHandles(out _, out _iconHandle);
                    _iconDefaultHandle = _iconHandle.CriticalGetHandle();
                }
                _iconCurrentHandle = _iconDefaultHandle;
            }
        }

        private void UpdateIcon(bool showIconInTray, bool isTransparent = false)
        {
            lock (_syncObj)
            {
                if (DesignerHelper.IsInDesignMode) return;

                _isTransparent = isTransparent;
                var data = new NOTIFYICONDATA
                {
                    uCallbackMessage = WmTrayMouseMessage,
                    uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
                    hWnd = _messageWindowHandle,
                    uID = _id,
                    dwInfoFlags = NativeMethods.NIF_TIP,
                    hIcon = isTransparent ? IntPtr.Zero : _iconCurrentHandle,
                    szTip = Text
                };

                if (showIconInTray)
                {
                    if (!_added)
                    {
                        UnsafeNativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, data);
                        _added = true;
                    }
                    else
                    {
                        UnsafeNativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, data);
                    }
                }
                else if (_added)
                {
                    UnsafeNativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, data);
                    _added = false;
                }
            }
        }

        private void RegisterClass()
        {
            _windowClassName = $"HandyControl.Controls.NotifyIcon{Guid.NewGuid()}";
            var wndclass = new WNDCLASS
            {
                style = 0,
                lpfnWndProc = _callback,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = IntPtr.Zero,
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = "",
                lpszClassName = _windowClassName
            };

            UnsafeNativeMethods.RegisterClass(wndclass);
            _wmTaskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
            _messageWindowHandle = UnsafeNativeMethods.CreateWindowEx(0, _windowClassName, "", 0, 0, 0, 1, 1,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        private IntPtr Callback(IntPtr hWnd, int msg, IntPtr wparam, IntPtr lparam)
        {
            if (IsLoaded)
            {
                if (msg == _wmTaskbarCreated)
                {
                    UpdateIcon(true);
                }
                else
                {
                    switch (lparam.ToInt64())
                    {
                        case NativeMethods.WM_LBUTTONDBLCLK:
                            WmMouseDown(MouseButton.Left, 2);
                            break;
                        case NativeMethods.WM_LBUTTONUP:
                            WmMouseUp(MouseButton.Left);
                            break;
                        case NativeMethods.WM_RBUTTONUP:
                            ShowContextMenu();
                            WmMouseUp(MouseButton.Right);
                            break;
                    }
                }
            }

            return UnsafeNativeMethods.DefWindowProc(hWnd, msg, wparam, lparam);
        }

        private void WmMouseDown(MouseButton button, int clicks)
        {
            if (clicks == 2)
            {
                RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
                {
                    RoutedEvent = MouseDoubleClickEvent
                });
                _doubleClick = true;
            }
        }

        private void WmMouseUp(MouseButton button)
        {
            if (!_doubleClick && button == MouseButton.Left)
            {
                RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
                {
                    RoutedEvent = ClickEvent
                });
            }
            _doubleClick = false;
        }

        private void ShowContextMenu()
        {

            if (ContextContent != null)
            {
                if (_contextContent == null)
                {
                    _contextContent = new Popup
                    {
                        Placement = PlacementMode.Mouse,
                        AllowsTransparency = true,
                        StaysOpen = false
                    };
                }

                _contextContent.Child = new ContentControl
                {
                    Content = ContextContent
                };
                _contextContent.IsOpen = true;
                var handle = IntPtr.Zero;
                var hwndSource = (HwndSource)PresentationSource.FromVisual(_contextContent.Child);
                if (hwndSource != null)
                {
                    handle = hwndSource.Handle;
                }
                UnsafeNativeMethods.SetForegroundWindow(handle);
            }
            else if (ContextMenu != null)
            {
                ContextMenu.Placement = PlacementMode.Mouse;
                ContextMenu.IsOpen = true;

                var handle = IntPtr.Zero;
                var hwndSource = (HwndSource)PresentationSource.FromVisual(ContextMenu);
                if (hwndSource != null)
                {
                    handle = hwndSource.Handle;
                }
                UnsafeNativeMethods.SetForegroundWindow(handle);
            }
        }

        public static readonly RoutedEvent ClickEvent =
            EventManager.RegisterRoutedEvent("Click", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(NotifyIcon));

        public event RoutedEventHandler Click
        {
            add => AddHandler(ClickEvent, value);
            remove => RemoveHandler(ClickEvent, value);
        }

        public static readonly RoutedEvent MouseDoubleClickEvent =
            EventManager.RegisterRoutedEvent("MouseDoubleClick", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(NotifyIcon));

        public event RoutedEventHandler MouseDoubleClick
        {
            add => AddHandler(MouseDoubleClickEvent, value);
            remove => RemoveHandler(MouseDoubleClickEvent, value);
        }       

        private void Dispose(bool disposing)
        {
            if (_isDisposed) return;
            if (disposing)
            {
                if (_dispatcherTimer != null && IsBlink)
                {
                    _dispatcherTimer.Stop();
                }
                UpdateIcon(false);
            }

            _isDisposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void CloseContextControl()
        {
            if (_contextContent != null)
            {
                _contextContent.IsOpen = false;
            }
            else if (ContextMenu != null)
            {
                ContextMenu.IsOpen = false;
            }
        }
    }
}
