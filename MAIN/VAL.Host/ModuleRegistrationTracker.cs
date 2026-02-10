using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace VAL.Host
{
    public sealed class ModuleRegistrationTracker
    {
        private readonly ConditionalWeakTable<object, HashSet<string>> _seenByCore = new();

        public bool TryMarkRegistered(object coreKey, string moduleKey)
        {
            if (coreKey == null || string.IsNullOrWhiteSpace(moduleKey))
                return false;

            var set = _seenByCore.GetOrCreateValue(coreKey);
            lock (set)
            {
                return set.Add(moduleKey);
            }
        }
    }
}
