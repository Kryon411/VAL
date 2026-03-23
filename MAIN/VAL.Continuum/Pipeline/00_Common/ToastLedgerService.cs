using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VAL.Continuum.Pipeline.Truth;
using VAL.Host.Json;

namespace VAL.Continuum.Pipeline
{
    /// <summary>
    /// Best-effort per-chat toast ledger to ensure "once per chat" rules survive restarts.
    /// Storage: &lt;ChatDir&gt;\\ToastLedger.json
    /// </summary>
    public sealed class ToastLedgerService : IToastLedger
    {
        private const string LedgerFileName = "ToastLedger.json";
        private readonly object _gate = new();
        private readonly ITruthStore _truthStore;

        public ToastLedgerService(ITruthStore truthStore)
        {
            _truthStore = truthStore ?? throw new ArgumentNullException(nameof(truthStore));
        }

        private sealed class LedgerModel
        {
            public int version { get; set; } = 1;
            public HashSet<string> shown { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private string GetLedgerPath(string chatId)
        {
            var dir = _truthStore.EnsureChatDir(chatId);
            return Path.Combine(dir, LedgerFileName);
        }

        public bool TryMarkShown(string chatId, string toastId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return false;
            if (string.IsNullOrWhiteSpace(toastId)) return false;

            try
            {
                lock (_gate)
                {
                    var model = Load(chatId);
                    if (model.shown.Contains(toastId))
                        return false;

                    model.shown.Add(toastId);
                    Save(chatId, model);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public bool HasShown(string chatId, string toastId)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return false;
            if (string.IsNullOrWhiteSpace(toastId)) return false;

            try
            {
                lock (_gate)
                {
                    var model = Load(chatId);
                    return model.shown.Contains(toastId);
                }
            }
            catch
            {
                return false;
            }
        }

        private LedgerModel Load(string chatId)
        {
            try
            {
                var path = GetLedgerPath(chatId);
                if (!File.Exists(path))
                    return new LedgerModel();

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return new LedgerModel();

                var model = JsonSerializer.Deserialize<LedgerModel>(json);
                return model ?? new LedgerModel();
            }
            catch
            {
                return new LedgerModel();
            }
        }

        private void Save(string chatId, LedgerModel model)
        {
            try
            {
                var path = GetLedgerPath(chatId);
                var json = JsonSerializer.Serialize(model, ValJsonOptions.Default);
                AtomicFile.WriteAllTextAtomic(path, json);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
