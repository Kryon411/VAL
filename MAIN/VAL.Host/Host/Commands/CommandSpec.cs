using System;

namespace VAL.Host.Commands
{
    internal sealed record CommandSpec(
        string Type,
        string Module,
        string[] RequiredFields,
        Action<HostCommand> Handler
    );
}
