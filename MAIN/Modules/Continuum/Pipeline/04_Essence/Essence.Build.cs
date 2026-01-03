using System;

namespace VAL.Continuum.Pipeline.Essence
{
    public static class EssenceBuild
    {
        // Essence-M only (Pulse only).
        // Input is WorkingSet (~25k chars) built by Gate.BuildWorkingSet.
        public static string BuildEssenceM(string chatId, string workingSet)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                throw new ArgumentNullException(nameof(chatId));

            if (string.IsNullOrWhiteSpace(workingSet))
                return string.Empty;

            // Stage order:
            // 1) Clean (mechanical text hygiene; no intelligence)
            // 2) Assemble (condensed transcript formatting; preserve flow)
            var cleaned = EssenceClean.CleanWorkingSet(workingSet);

            var essence = EssenceAssemble.AssembleEssenceM(chatId, cleaned);

            return essence ?? string.Empty;
        }
    }
}
