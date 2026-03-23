using System;
using System.Collections.Generic;
using System.Linq;
using VAL.Host.Commands;

namespace VAL.Host
{
    internal sealed class CommandRegistryComposer
    {
        private readonly IReadOnlyList<ICommandRegistryContributor> _contributors;

        public CommandRegistryComposer(IEnumerable<ICommandRegistryContributor> contributors)
        {
            ArgumentNullException.ThrowIfNull(contributors);
            _contributors = contributors.ToList();
        }

        public void Register(CommandRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            foreach (var contributor in _contributors)
            {
                contributor.Register(registry);
            }
        }
    }
}
