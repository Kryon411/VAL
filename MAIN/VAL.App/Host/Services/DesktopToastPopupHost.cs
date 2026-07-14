using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace VAL.App.Host.Services
{
    internal sealed class DesktopToastPopupHost
    {
        private const double RightInset = 30;
        private const double BottomInset = 60;

        private readonly List<DesktopToastInstance> _items = [];
        private Popup? _popup;
        private StackPanel? _stack;
        private Window? _window;

        public bool IsInitialized => _popup != null && _stack != null && _window != null;

        public void Initialize(Window hostWindow)
        {
            ArgumentNullException.ThrowIfNull(hostWindow);

            if (_popup != null)
            {
                return;
            }

            _window = hostWindow;
            _stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0)
            };

            _popup = new Popup
            {
                AllowsTransparency = true,
                PlacementTarget = hostWindow,
                Placement = PlacementMode.Relative,
                StaysOpen = true,
                Child = new Border
                {
                    Background = Brushes.Transparent,
                    Child = _stack
                }
            };

            hostWindow.SizeChanged += (_, __) => Reposition();
            hostWindow.LocationChanged += (_, __) => Reposition();
            hostWindow.StateChanged += (_, __) => Reposition();

            Reposition();
        }

        public void Show(DesktopToastInstance item)
        {
            if (!IsInitialized || _stack == null || _popup == null)
            {
                return;
            }

            _items.Insert(0, item);
            _stack.Children.Insert(0, item.View);
            _popup.IsOpen = true;
            Reposition();
        }

        public void Remove(DesktopToastInstance item)
        {
            if (!IsInitialized)
            {
                return;
            }

            RemoveCore(item);
            RefreshPopupState();
        }

        public void RemoveGroup(string groupKey)
        {
            if (!IsInitialized || string.IsNullOrWhiteSpace(groupKey))
            {
                return;
            }

            var matches = _items
                .Where(item => !string.IsNullOrWhiteSpace(item.GroupKey) &&
                               string.Equals(item.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var match in matches)
            {
                RemoveCore(match);
            }

            RefreshPopupState();
        }

        private void RemoveCore(DesktopToastInstance item)
        {
            try
            {
                item.Timer?.Stop();
            }
            catch
            {
            }

            item.Timer = null;
            _items.Remove(item);
            _stack?.Children.Remove(item.View);
        }

        private void RefreshPopupState()
        {
            if (_popup == null || _stack == null)
            {
                return;
            }

            Reposition();
            _popup.IsOpen = _stack.Children.Count > 0;
        }

        private void Reposition()
        {
            if (_popup == null || _stack == null || _window == null)
            {
                return;
            }

            try
            {
                _stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var desired = _stack.DesiredSize;
                var work = SystemParameters.WorkArea;

                var windowLeft = _window.Left;
                var windowTop = _window.Top;
                var windowRight = windowLeft + _window.ActualWidth;
                var windowBottom = windowTop + _window.ActualHeight;

                var safeRightScreen = Math.Min(windowRight, work.Right) - RightInset;
                var safeBottomScreen = Math.Min(windowBottom, work.Bottom) - BottomInset;

                var safeRightInWindow = safeRightScreen - windowLeft;
                var safeBottomInWindow = safeBottomScreen - windowTop;

                var x = safeRightInWindow - desired.Width;
                var y = safeBottomInWindow - desired.Height;

                if (x < 0)
                {
                    x = 0;
                }

                if (y < 0)
                {
                    y = 0;
                }

                _popup.HorizontalOffset = x;
                _popup.VerticalOffset = y;
            }
            catch
            {
                // Never let toast layout break the host UI.
            }
        }
    }
}
