using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace VAL.Host
{
    public static class ToastManager
    {
        private static Popup? _popup;
        private static StackPanel? _stack;
        private static Window? _window;

        // SoftGlass theme constants (match the Control Centre dock aesthetic)
        private static readonly CornerRadius SoftGlassCorner = new CornerRadius(18);
        private static readonly Thickness SoftGlassPadding = new Thickness(16, 12, 16, 14);
        private static readonly Thickness SoftGlassMargin = new Thickness(0, 0, 0, 14);

        private static LinearGradientBrush CreateSoftGlassCardBackground()
        {
            // Dock reference:
            //   background-color: rgba(2,10,20,0.96)
            //   overlay sheen: linear-gradient(135deg, rgba(255,255,255,0.06), rgba(255,255,255,0.02))
            // Approximate in WPF as a single gradient between two nearby deep-navy colors.
            return new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(245, 17, 25, 34), 0.0),
                    new GradientStop(Color.FromArgb(245, 7, 15, 25), 1.0)
                }
            };
        }

        private static SolidColorBrush CreateSoftGlassBorderBrush()
        {
            // Matches rgba(120, 180, 240, 0.16)
            return new SolidColorBrush(Color.FromArgb(41, 120, 180, 240));
        }

        private static DropShadowEffect CreateSoftGlassShadow()
        {
            // Deep shadow (lift)
            return new DropShadowEffect
            {
                Color = Color.FromArgb(220, 0, 0, 0),
                Direction = 270,
                ShadowDepth = 10,
                BlurRadius = 28,
                Opacity = 0.90
            };
        }

        private static DropShadowEffect CreateSoftGlassGlow()
        {
            // Soft cyan glow around the card
            return new DropShadowEffect
            {
                Color = Color.FromArgb(200, 20, 120, 180),
                Direction = 0,
                ShadowDepth = 0,
                BlurRadius = 24,
                Opacity = 0.60
            };
        }

        private static Border WrapInSoftGlassChrome(UIElement content)
{
    // Flat SoftGlass chrome: no drop shadow, no glow.
    // Keep the same border recipe used by the Dock (rgba(120,180,240,0.16)).
    //
    // IMPORTANT: In WPF, BorderThickness is in device-independent pixels (DIPs).
    // On common Windows scaling (125%/150%), a thickness of 1 DIP becomes >1 physical pixel,
    // which can make the border look heavier/brighter than the CSS 1px border used by the Dock.
    // We therefore set the border thickness to ~1 physical pixel using the element's DPI scale.
    var b = new Border
    {
        MaxWidth = 420,
        MinWidth = 260,
        Background = CreateSoftGlassCardBackground(),
        BorderBrush = CreateSoftGlassBorderBrush(),
        BorderThickness = new Thickness(1), // will be corrected to 1 physical px on Loaded
        CornerRadius = SoftGlassCorner,
        Padding = SoftGlassPadding,
        Margin = SoftGlassMargin,
        Opacity = 0,
        SnapsToDevicePixels = true,
        UseLayoutRounding = true,
        Child = content
    };

    b.Loaded += (_, __) =>
    {
        var dpi = VisualTreeHelper.GetDpi(b);
        var onePx = 1.0 / dpi.DpiScaleX;
        b.BorderThickness = new Thickness(onePx);
    };

    return b;
}


        private static readonly ControlTemplate SoftGlassButtonTemplate = BuildSoftGlassButtonTemplate();

        private static ControlTemplate BuildSoftGlassButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, SoftGlassCorner);
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            // FrameworkElementFactory.SetBinding requires a BindingBase; use TemplatedParent bindings.
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            // Use Button.Padding as "inner padding".
            presenter.SetBinding(ContentPresenter.MarginProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private static void ApplySoftGlassButtonStyle(Button b)
        {
            // Match dock button feel
            b.Template = SoftGlassButtonTemplate;
            b.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(18, 255, 255, 255), 0.0),
                    new GradientStop(Color.FromArgb(245, 4, 18, 30), 1.0),
                }
            };
            b.Foreground = new SolidColorBrush(Color.FromArgb(176, 245, 251, 255));
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(56, 120, 180, 240));
            b.BorderThickness = new Thickness(1);
        }

        // Launch quiet period: suppress passive system toasts during initial startup.
        // User-invoked toasts should bypass this policy at call sites.
        private static DateTime _initializedUtc = DateTime.MinValue;
        private static readonly TimeSpan LaunchQuietPeriod = TimeSpan.FromSeconds(12);

        public static bool IsLaunchQuietPeriodActive
        {
            get
            {
                if (_initializedUtc == DateTime.MinValue) return true;
                return (DateTime.UtcNow - _initializedUtc) < LaunchQuietPeriod;
            }
        }

        public enum ToastDurationBucket
        {
            XS,
            S,
            M,
            L,
            LShort,
            XL,
            Sticky
        }

        private sealed class ToastMeta
        {
            public string? GroupKey;
            public DispatcherTimer? Timer;
        }

        // Safe insets (inside the window / work area) so toasts never clip into the taskbar.
        private const double RightInset = 30;
        private const double BottomInset = 60;

        // Burst-dedupe: prevents identical toasts stacking when events fire multiple times.
        private static readonly object _dedupeLock = new object();
        private static readonly Dictionary<string, DateTime> _recentToastKeys = new Dictionary<string, DateTime>();
        private static readonly TimeSpan _dedupeWindow = TimeSpan.FromMilliseconds(2000);

        private static bool ShouldSuppressToast(string title, string? subtitle)
        {
            var key = (title ?? string.Empty).Trim() + "\n" + (subtitle ?? string.Empty).Trim();
            var now = DateTime.UtcNow;
            lock (_dedupeLock)
            {
                var stale = _recentToastKeys.Where(kv => (now - kv.Value) > _dedupeWindow)
                                           .Select(kv => kv.Key)
                                           .ToList();
                foreach (var k in stale) _recentToastKeys.Remove(k);

                if (_recentToastKeys.TryGetValue(key, out var last) && (now - last) <= _dedupeWindow)
                    return true;

                _recentToastKeys[key] = now;
                return false;
            }
        }

        private static TimeSpan? GetLifetime(ToastDurationBucket bucket)
        {
            switch (bucket)
            {
                case ToastDurationBucket.XS: return TimeSpan.FromSeconds(2);
                case ToastDurationBucket.S: return TimeSpan.FromSeconds(5);
                case ToastDurationBucket.M: return TimeSpan.FromSeconds(10);
                case ToastDurationBucket.L: return TimeSpan.FromSeconds(14);
                case ToastDurationBucket.LShort: return TimeSpan.FromSeconds(9);
                case ToastDurationBucket.XL: return TimeSpan.FromSeconds(22);
                case ToastDurationBucket.Sticky: return null;
                default: return TimeSpan.FromSeconds(10);
            }
        }

        public static void Initialize(Window w)
        {
            if (_initializedUtc == DateTime.MinValue)
                _initializedUtc = DateTime.UtcNow;

            if (_popup != null) return;

            _window = w;

            _stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 0, 0, 0)
            };

            _popup = new Popup
            {
                AllowsTransparency = true,
                PlacementTarget = w,
                Placement = PlacementMode.Relative,
                StaysOpen = true
            };

            w.SizeChanged += (_, __) => Reposition();
            w.LocationChanged += (_, __) => Reposition();
            w.StateChanged += (_, __) => Reposition();

            _popup.Child = new Border
            {
                Background = Brushes.Transparent,
                Child = _stack
            };

            Reposition();
        }

        private static void Reposition()
        {
            if (_popup == null || _stack == null || _window == null) return;

            try
            {
                _stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var desired = _stack.DesiredSize;

                var work = SystemParameters.WorkArea;

                // Window bounds in screen coordinates (DIPs).
                var windowLeft = _window.Left;
                var windowTop = _window.Top;
                var windowRight = windowLeft + _window.ActualWidth;
                var windowBottom = windowTop + _window.ActualHeight;

                // Clamp the toast anchor point to the work area so we never clip into the taskbar.
                var safeRightScreen = Math.Min(windowRight, work.Right) - RightInset;
                var safeBottomScreen = Math.Min(windowBottom, work.Bottom) - BottomInset;

                // Convert the safe anchor point into window-relative coordinates (Popup.PlacementMode.Relative).
                var safeRightInWindow = safeRightScreen - windowLeft;
                var safeBottomInWindow = safeBottomScreen - windowTop;

                // Anchor the *bottom-right* of the toast stack to the safe point so the stack grows upward.
                var x = safeRightInWindow - desired.Width;
                var y = safeBottomInWindow - desired.Height;

                if (x < 0) x = 0;
                if (y < 0) y = 0;

                _popup.HorizontalOffset = x;
                _popup.VerticalOffset = y;
            }
            catch
            {
                // Never let toast layout break the host UI.
            }
        }

        // Compatibility API: shows a standard toast using the default bucket (M).
        public static void Show(string title, string? subtitle = null)
        {
            ShowCatalog(title, subtitle, ToastDurationBucket.M);
        }

        // Catalog API: message-only toast (no subtitle).
        public static void ShowCatalog(string message, ToastDurationBucket duration, string? groupKey = null, bool replaceGroup = false)
        {
            ShowCatalog(message, null, duration, groupKey, replaceGroup);
        }

        // Catalog API: supports title + subtitle when needed.
        public static void ShowCatalog(
            string title,
            string? subtitle,
            ToastDurationBucket duration,
            string? groupKey = null,
            bool replaceGroup = false,
            bool bypassBurstDedupe = false)
        {
            if (_popup == null || _stack == null) return;
            if (!bypassBurstDedupe && ShouldSuppressToast(title ?? string.Empty, subtitle)) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.Invoke(() =>
            {
                if (_popup == null || _stack == null) return;

                if (!string.IsNullOrWhiteSpace(groupKey) && replaceGroup)
                    RemoveGroupToasts(groupKey!);

                var panel = new StackPanel();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = title,
                        // Match dock primary text (#f5fbff)
                        Foreground = new SolidColorBrush(Color.FromArgb(176, 245, 251, 255)),
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                if (!string.IsNullOrWhiteSpace(subtitle))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = subtitle,
                        // Match dock secondary text
                        Foreground = new SolidColorBrush(Color.FromArgb(143, 170, 190, 210)),
                        FontSize = 11,
                        Margin = new Thickness(0, 6, 0, 0),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                var toast = WrapInSoftGlassChrome(panel);

                var meta = new ToastMeta { GroupKey = groupKey };
                toast.Tag = meta;

                _stack.Children.Insert(0, toast);

                _popup.IsOpen = true;
                Reposition();

                // Fade in
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
                toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                var lifetime = GetLifetime(duration);
                if (lifetime == null)
                    return; // Sticky

                // Auto close after delay
                var timer = new DispatcherTimer { Interval = lifetime.Value };
                meta.Timer = timer;

                timer.Tick += (_, __) =>
                {
                    try { timer.Stop(); } catch { }

                    var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600));
                    fadeOut.Completed += (_, ___) =>
                    {
                        try
                        {
                            _stack.Children.Remove(toast);
                            Reposition();
                            if (_stack.Children.Count == 0)
                            {
                                _popup.IsOpen = false;
                            }
                        }
                        catch
                        {
                            // ignore
                        }
                    };

                    toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                };

                timer.Start();
            });
        }

        private static void RemoveGroupToasts(string groupKey)
        {
            if (_stack == null || _popup == null) return;

            try
            {
                var victims = new List<UIElement>();
                foreach (UIElement child in _stack.Children)
                {
                    if (child is Border b && b.Tag is ToastMeta meta)
                    {
                        if (!string.IsNullOrWhiteSpace(meta.GroupKey) &&
                            string.Equals(meta.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase))
                        {
                            victims.Add(child);
                        }
                    }
                }

                foreach (var v in victims)
                {
                    try
                    {
                        if (v is Border b && b.Tag is ToastMeta meta && meta.Timer != null)
                            meta.Timer.Stop();
                    }
                    catch { }

                    try { _stack.Children.Remove(v); } catch { }
                }

                Reposition();
                if (_stack.Children.Count == 0)
                    _popup.IsOpen = false;
            }
            catch
            {
                // ignore
            }
        }

        public static void ShowActions(string title, string subtitle, params (string Label, Action OnClick)[] actions)
        {
            ShowActions(title, subtitle, null, false, sticky: false, actions);
        }

        public static void ShowActions(string title, string subtitle, string? groupKey, bool replaceGroup, params (string Label, Action OnClick)[] actions)
        {
            ShowActions(title, subtitle, groupKey, replaceGroup, sticky: false, actions);
        }

        /// <summary>
        /// Action toast with explicit stickiness control.
        /// Sticky action toasts remain visible until a button is clicked.
        /// </summary>
        public static void ShowActions(string title, string subtitle, string? groupKey, bool replaceGroup, bool sticky, params (string Label, Action OnClick)[] actions)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_popup == null || _stack == null) return;
                if (ShouldSuppressToast(title ?? string.Empty, subtitle ?? string.Empty)) return;

                if (replaceGroup && !string.IsNullOrWhiteSpace(groupKey))
                    RemoveGroupToasts(groupKey);


                bool isNewChatAssist = replaceGroup && !string.IsNullOrWhiteSpace(groupKey) &&
                    string.Equals(groupKey, "continuum_guidance", StringComparison.OrdinalIgnoreCase);

                // Sticky behavior is used for:
                // - New Chat Assist (Prelude prompt)
                // - Explicitly sticky prompts (e.g., Chronicle guidance)
                bool isSticky = sticky || isNewChatAssist;

                var inner = new StackPanel { Orientation = Orientation.Vertical };

                var titleBlock = new TextBlock
                {
                    Text = title ?? string.Empty,
                    Foreground = new SolidColorBrush(Color.FromArgb(176, 245, 251, 255)),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };

                inner.Children.Add(titleBlock);

                if (!string.IsNullOrWhiteSpace(subtitle))
                {
                    inner.Children.Add(new TextBlock
                    {
                        Text = subtitle ?? string.Empty,
                        Foreground = new SolidColorBrush(Color.FromArgb(143, 170, 190, 210)),
                        FontSize = 11,
                        Margin = new Thickness(0, 6, 0, 0),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                // Wrap content in the same SoftGlass chrome as the dock.
                var toast = WrapInSoftGlassChrome(inner);

                var meta = new ToastMeta { GroupKey = groupKey };
                toast.Tag = meta;


                if (actions != null && actions.Length > 0)
                {
                    var btnRow = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 12, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Right
                    };

                    foreach (var a in actions)
                    {
                        var b = new Button
                        {
                            Content = a.Label ?? string.Empty,
                            Margin = new Thickness(8, 0, 0, 0),
                            Padding = new Thickness(14, 6, 14, 6),
                            MinWidth = 88,
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold
                        };

                        ApplySoftGlassButtonStyle(b);

                        b.Click += (_, __) =>
                        {
                            try { a.OnClick?.Invoke(); } catch { }

                            try
                            {
                                if (_stack != null)
                                {
                                    _stack.Children.Remove(toast);
                                    Reposition();
                                    if (_stack.Children.Count == 0 && _popup != null) _popup.IsOpen = false;
                                }
                            }
                            catch { }
                        };

                        btnRow.Children.Add(b);
                    }

                    inner.Children.Add(btnRow);
                }

                // Stack position:
                // - For New Chat Assist, place at the bottom of the toast stack (non-interruptive).
                // - For other action toasts, keep current behavior (top).
                if (isNewChatAssist)
                    _stack.Children.Add(toast);
                else
                    _stack.Children.Insert(0, toast);

                _popup.IsOpen = true;

                Reposition();

                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
                toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                // Lifetime:
                // - New Chat Assist should stay until the user clicks a button (no auto-timeout).
                // - Other action toasts can still auto-dismiss.
                if (!isSticky)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                    meta.Timer = timer;
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();

                        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
                        fadeOut.Completed += (_, __) =>
                        {
                            _stack.Children.Remove(toast);
                            Reposition();
                            if (_stack.Children.Count == 0)
                            {
                                _popup.IsOpen = false;
                            }
                        };

                        toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    };

                    timer.Start();
                }
            });
        }

        /// <summary>
        /// Dismiss (remove) all toasts for a given groupKey.
        /// </summary>
        public static void DismissGroup(string groupKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(groupKey)) return;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    try { RemoveGroupToasts(groupKey); } catch { }
                });
            }
            catch
            {
                // ignore
            }
        }


    }
}