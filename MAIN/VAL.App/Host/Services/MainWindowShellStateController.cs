using System;
using System.Text.Json;
using System.Windows;

using VAL.App.State;
using VAL.Contracts;
using VAL.Host.WebMessaging;

namespace VAL.App.Host.Services
{
    public sealed class MainWindowShellStateController
    {
        private static readonly TimeSpan LauncherDebounceWindow = TimeSpan.FromMilliseconds(250);
        private const string HostSource = "host";
        private const string DockOpenType = "dock.open";
        private const string DockCloseType = "dock.close";
        private const string DockLayoutEnableType = "dock.layout.enable";
        private const string DockLayoutDisableType = "dock.layout.disable";
        private const string DockUiStateDataType = "dock.ui_state.data";
        private const string DockUiStateSetType = "dock.ui_state.set";
        private const string DockStateType = "dock.state";

        private ControlCentreUiState _state = ControlCentreUiState.Default;
        private DateTime _lastLauncherClickUtc = DateTime.MinValue;

        public MainWindowShellStateController(IControlCentreUiStateStore stateStore)
        {
            StateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        }

        private IControlCentreUiStateStore StateStore { get; }

        public bool IsDockOpen => _state.Dock.IsOpen;

        public bool IsLayoutModeEnabled => _state.LayoutMode;

        public void Load()
        {
            _state = StateStore.Load().Normalize();
        }

        public void Save()
        {
            StateStore.Save(_state);
        }

        public ControlCentreUiState CreateSnapshot()
        {
            return new ControlCentreUiState
            {
                Version = _state.Version,
                ControlCentre = _state.ControlCentre,
                Dock = new DockGeometryState
                {
                    IsOpen = _state.Dock.IsOpen,
                    X = _state.Dock.X,
                    Y = _state.Dock.Y,
                    W = _state.Dock.W,
                    H = _state.Dock.H,
                },
                LayoutMode = _state.LayoutMode,
            };
        }

        public GeometryState ResolveControlCentreGeometry(Func<GeometryState> defaultGeometryFactory)
        {
            ArgumentNullException.ThrowIfNull(defaultGeometryFactory);

            if (!_state.ControlCentre.HasPosition)
            {
                _state.ControlCentre = defaultGeometryFactory();
            }

            return _state.ControlCentre;
        }

        public void UpdateControlCentreGeometry(GeometryState geometry)
        {
            _state.ControlCentre = geometry;
        }

        public bool TryHandleLauncherClick(DateTime nowUtc, out MessageEnvelope envelope, out bool requiresDockStateSync)
        {
            envelope = null!;
            requiresDockStateSync = false;

            if ((nowUtc - _lastLauncherClickUtc) < LauncherDebounceWindow)
            {
                return false;
            }

            _lastLauncherClickUtc = nowUtc;
            _state.Dock.IsOpen = !_state.Dock.IsOpen;
            requiresDockStateSync = _state.Dock.IsOpen;
            envelope = CreateDockVisibilityEnvelope();
            return true;
        }

        public MessageEnvelope ToggleLayoutMode()
        {
            _state.LayoutMode = !_state.LayoutMode;
            return CreateLayoutModeEnvelope();
        }

        public MessageEnvelope CreateLayoutModeEnvelope()
        {
            return CreateHostEvent(
                _state.LayoutMode ? DockLayoutEnableType : DockLayoutDisableType,
                new { source = HostSource });
        }

        public MessageEnvelope CreateDockUiStateEnvelope()
        {
            var dock = _state.Dock;
            return CreateHostEvent(
                DockUiStateDataType,
                new
                {
                    source = HostSource,
                    x = dock.X,
                    y = dock.Y,
                    w = dock.W,
                    h = dock.H,
                    isOpen = dock.IsOpen,
                });
        }

        public bool TryApplyDockMessage(WebMessageEnvelope envelope, Rect virtualScreenBounds)
        {
            try
            {
                if (!envelope.TryGetParsedEnvelope(out var parsedEnvelope))
                {
                    return false;
                }

                var type = parsedEnvelope.Name?.Trim();
                if (string.IsNullOrWhiteSpace(type))
                {
                    return false;
                }

                if (string.Equals(type, DockStateType, StringComparison.Ordinal) &&
                    TryReadIsOpen(parsedEnvelope, out var dockIsOpen))
                {
                    _state.Dock.IsOpen = dockIsOpen;
                    return true;
                }

                if (!string.Equals(type, DockUiStateSetType, StringComparison.Ordinal))
                {
                    return false;
                }

                if (TryReadIsOpen(parsedEnvelope, out var isOpen))
                {
                    _state.Dock.IsOpen = isOpen;
                }

                UpdateDockGeometryFromMessage(parsedEnvelope, virtualScreenBounds);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private MessageEnvelope CreateDockVisibilityEnvelope()
        {
            return CreateHostEvent(
                _state.Dock.IsOpen ? DockOpenType : DockCloseType,
                new { source = HostSource });
        }

        private static MessageEnvelope CreateHostEvent(string name, object payload)
        {
            return new MessageEnvelope
            {
                Type = WebMessageTypes.Event,
                Name = name,
                Source = HostSource,
                Payload = JsonSerializer.SerializeToElement(payload),
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
        }

        private void UpdateDockGeometryFromMessage(MessageEnvelope envelope, Rect virtualScreenBounds)
        {
            if (!TryGetPayloadRoot(envelope, out var root))
            {
                return;
            }

            var x = TryReadDoubleProperty(root, "x");
            var y = TryReadDoubleProperty(root, "y");
            var w = TryReadDoubleProperty(root, "w");
            var h = TryReadDoubleProperty(root, "h");

            if (x.HasValue) _state.Dock.X = x.Value;
            if (y.HasValue) _state.Dock.Y = y.Value;
            if (w.HasValue) _state.Dock.W = w.Value;
            if (h.HasValue) _state.Dock.H = h.Value;

            ClampDockGeometry(virtualScreenBounds);
        }

        private void ClampDockGeometry(Rect virtualScreenBounds)
        {
            var dock = _state.Dock;
            var left = virtualScreenBounds.Left;
            var top = virtualScreenBounds.Top;
            var right = virtualScreenBounds.Right;
            var bottom = virtualScreenBounds.Bottom;
            var maxWidth = Math.Max(360d, virtualScreenBounds.Width);
            var maxHeight = Math.Max(180d, virtualScreenBounds.Height);

            dock.W = Math.Clamp(dock.W, 360d, maxWidth);
            dock.H = Math.Clamp(dock.H, 180d, maxHeight);
            dock.X = Math.Clamp(dock.X, left, Math.Max(left, right - dock.W));
            dock.Y = Math.Clamp(dock.Y, top, Math.Max(top, bottom - dock.H));
        }

        private static bool TryReadIsOpen(MessageEnvelope envelope, out bool isOpen)
        {
            isOpen = false;

            if (!TryGetPayloadRoot(envelope, out var root))
            {
                return false;
            }

            if (root.TryGetProperty("isOpen", out var direct) && TryReadBoolean(direct, out isOpen))
            {
                return true;
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return payload.TryGetProperty("isOpen", out var payloadIsOpen) &&
                   TryReadBoolean(payloadIsOpen, out isOpen);
        }

        private static bool TryReadBoolean(JsonElement value, out bool result)
        {
            result = false;

            if (value.ValueKind == JsonValueKind.True)
            {
                result = true;
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                result = false;
                return true;
            }

            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out var parsed))
            {
                result = parsed;
                return true;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                result = number != 0;
                return true;
            }

            return false;
        }

        private static double? TryReadDoubleProperty(JsonElement root, string name)
        {
            if (TryReadDoubleValue(root, name, out var direct))
            {
                return direct;
            }

            if (root.TryGetProperty("payload", out var payload) &&
                payload.ValueKind == JsonValueKind.Object &&
                TryReadDoubleValue(payload, name, out var nested))
            {
                return nested;
            }

            return null;
        }

        private static bool TryReadDoubleValue(JsonElement root, string name, out double value)
        {
            value = 0;

            if (!root.TryGetProperty(name, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value))
            {
                return true;
            }

            if (property.ValueKind == JsonValueKind.String &&
                double.TryParse(property.GetString(), out var parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }

        private static bool TryGetPayloadRoot(MessageEnvelope envelope, out JsonElement root)
        {
            root = default;

            if (!envelope.Payload.HasValue || envelope.Payload.Value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = envelope.Payload.Value;
            return true;
        }
    }
}
