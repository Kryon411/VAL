using System;
using System.Collections.Generic;

namespace VAL.Continuum.Pipeline.Truth
{
    public enum TruthRole
    {
        Unknown = 0,
        User = 1,
        Assistant = 2
    }

    public sealed class TruthMessage
    {
        public TruthRole Role { get; init; } = TruthRole.Unknown;

        // Raw text content for this message (text-only; uploads/screenshots are excluded upstream by capture policy).
        public string Text { get; init; } = string.Empty;

        // Optional: source line index in Truth.log (0-based).
        public int LineIndex { get; init; } = -1;

        // Optional: derived timestamp if your Truth.log embeds one.
        public DateTimeOffset? Timestamp { get; init; } = null;

        // Optional: marker tags found in assistant text (goal/checkpoint/milestone/*).
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    }

    public sealed class TruthView
    {
        public string ChatId { get; init; } = string.Empty;

        // Raw Truth.log text (as stored; may contain encoded newlines depending on capture format).
        public string RawText { get; init; } = string.Empty;

        // Parsed/normalized messages (structural only; no filtering/summarization).
        public IReadOnlyList<TruthMessage> Messages { get; init; } = Array.Empty<TruthMessage>();

        // Optional: a readable normalized transcript for inspection (Truth.view).
        public string ViewText { get; init; } = string.Empty;
    }
}