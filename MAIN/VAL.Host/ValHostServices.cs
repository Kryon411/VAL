using System;

namespace VAL.Host;

public static class ValHostServices
{
    private static IServiceProvider? _services;

    public static IServiceProvider? Services => _services;

    public static void Initialize(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }
}
