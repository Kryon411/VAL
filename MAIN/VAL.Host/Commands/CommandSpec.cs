using System;

namespace VAL.Host.Commands
{
    public sealed record CommandSpec(
        string Type,
        string Module,
        string[] RequiredFields,
        Action<HostCommand> Handler
    );
}
