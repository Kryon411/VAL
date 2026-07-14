using System;
using System.Text.Json;

using VAL.Continuum;
using VAL.Host.Commands;

using Xunit;

namespace VAL.Tests.Continuum
{
    public sealed class ContinuumCommandMessageTests
    {
        [Fact]
        public void FromMapsPayloadFields()
        {
            var command = CreateCommand(
                "envelope.type",
                "envelope-chat",
                """{"type":"payload.type","chatId":"payload-chat","requestId":"r1","capturedTurns":8,"enabled":true}""");

            var message = ContinuumCommandMessage.From(command);

            Assert.Equal("payload.type", message.Type);
            Assert.Equal("payload-chat", message.ChatId);
            Assert.Equal("r1", message.RequestId);
            Assert.Equal(8, message.CapturedTurns);
            Assert.True(message.Enabled);
        }

        [Fact]
        public void FromFallsBackToEnvelopeWhenPayloadIsMalformed()
        {
            var command = CreateCommand(
                "envelope.type",
                "envelope-chat",
                """{"capturedTurns":{"invalid":true}}""");

            var message = ContinuumCommandMessage.From(command);

            Assert.Equal("envelope.type", message.Type);
            Assert.Equal("envelope-chat", message.ChatId);
        }

        private static HostCommand CreateCommand(string type, string chatId, string json)
        {
            using var document = JsonDocument.Parse(json);
            return new HostCommand(
                type,
                json,
                chatId,
                new Uri("https://chatgpt.com/"),
                document.RootElement.Clone());
        }
    }
}
