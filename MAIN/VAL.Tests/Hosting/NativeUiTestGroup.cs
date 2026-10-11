using System;

using Xunit;

namespace VAL.Tests.Hosting;

// WPF initialization uses process-wide caches; keep native STA tests from racing each other.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NativeUiTestGroup
{
    public const string Name = "WPF native controls";
    public static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(60);
}
