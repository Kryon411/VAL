using System;
using System.Text.Json;
using System.Windows;

using VAL.App.Host.Services;
using VAL.Contracts;
using VAL.Host.WebMessaging;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class ControlCentreHeaderLayoutTests
    {
        [Fact]
        public void AcceptsOnlyBoundedContentFreeLauncherGeometry()
        {
            Assert.True(ControlCentreHeaderLayout.TryRead(Message(800, 12, 40, 40), out var launcher));
            Assert.Equal(new Rect(800, 12, 40, 40), launcher);
        }

        [Theory]
        [InlineData(-1, 12, 40, 40)]
        [InlineData(980, 12, 40, 40)]
        [InlineData(800, 590, 40, 40)]
        [InlineData(800, 12, 100, 40)]
        [InlineData(800, 12, 40, 0)]
        public void RejectsOutOfBoundsOrInvalidDimensions(double x, double y, double w, double h)
        {
            Assert.False(ControlCentreHeaderLayout.TryRead(Message(x, y, w, h), out _));
        }

        [Theory]
        [InlineData("event", "dock", "dock.ui_state.set")]
        [InlineData("command", "other", "dock.ui_state.set")]
        [InlineData("command", "dock", "dock.ui_state.get")]
        public void RejectsUnexpectedMessageKinds(string type, string source, string name)
        {
            Assert.False(ControlCentreHeaderLayout.TryRead(Message(800, 12, 40, 40, type, source, name), out _));
        }

        [Theory]
        [InlineData("https://accounts.google.com/")]
        [InlineData("https://chatgpt.com/auth/login")]
        [InlineData("https://chatgpt.com:8443/")]
        public void RejectsHeaderMeasurementsFromAuthenticationOrUntrustedOrigins(string source)
        {
            var message = Message(800, 12, 40, 40);
            Assert.False(ControlCentreHeaderLayout.TryRead(
                new WebMessageEnvelope("{}", new Uri(source), message.ParsedEnvelope), out _));
        }

        private static WebMessageEnvelope Message(double x, double y, double w, double h,
            string type = WebMessageTypes.Command, string source = "dock", string name = WebCommandNames.DockUiStateSet)
        {
            var envelope = new MessageEnvelope
            {
                Type = type,
                Source = source,
                Name = name,
                Payload = JsonSerializer.SerializeToElement(new
                {
                    headerAnchor = new { x, y, w, h, viewportWidth = 1000, viewportHeight = 600 }
                })
            };
            return new WebMessageEnvelope("{}", new Uri("https://chatgpt.com"), envelope);
        }
    }
}
