using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using VAL.Continuum.Pipeline.Truth;

namespace TruthDoctor
{
    internal sealed record Options(
        string Root,
        string? ChatId,
        bool Health,
        string? JsonPath,
        bool Compact,
        int CompactLines,
        int WarnMb,
        bool RepairTailFirst);

    internal sealed record CompactMeta(
        string ChatId,
        string TruthPath,
        long TruthBytes,
        int ParsedEntryCount,
        int CompactEntryCount,
        int CompactLines,
        DateTime GeneratedUtc);

    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (!TryParseArgs(args, out var options, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                    Console.Error.WriteLine(error);
                PrintUsage();
                return 1;
            }

            if (!options.Health && !options.Compact && string.IsNullOrWhiteSpace(options.JsonPath))
            {
                Console.Error.WriteLine("No action specified. Use --health, --compact, or --json.");
                PrintUsage();
                return 1;
            }

            var reports = new List<TruthHealthReport>();
            var chatIds = ResolveChatIds(options.Root, options.ChatId);
            foreach (var chatId in chatIds)
            {
                var chatDir = Path.Combine(options.Root, "Memory", "Chats", chatId);
                var truthPath = Path.Combine(chatDir, TruthStorage.TruthFileName);
                var repairLogPath = Path.Combine(chatDir, "Truth.repair.log");

                var report = TruthHealth.Build(chatId, truthPath, repairLogPath, options.RepairTailFirst);
                reports.Add(report);

                if (options.Health)
                    PrintHealth(report);

                if (options.WarnMb > 0)
                    PrintWarn(report, options.WarnMb, options, chatDir);

                if (options.Compact)
                    WriteCompactSnapshot(report, options, chatDir, truthPath);
            }

            if (!string.IsNullOrWhiteSpace(options.JsonPath))
                WriteJsonReport(options.JsonPath, options.Root, reports);

            return 0;
        }

        private static void PrintHealth(TruthHealthReport report)
        {
            var sizeMb = report.Bytes / (1024d * 1024d);
            var lastRepair = report.LastRepairUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "-";
            var bytesRemoved = report.LastRepairBytesRemoved?.ToString(CultureInfo.InvariantCulture) ?? "-";

            Console.WriteLine(
                $"CHAT {report.ChatId} " +
                $"size={sizeMb:0.##}MB " +
                $"phys={report.PhysicalLineCount} " +
                $"parsed={report.ParsedEntryCount} " +
                $"lastParsedLine={report.LastParsedPhysicalLineNumber} " +
                $"lastRepair={lastRepair} " +
                $"bytesRemoved={bytesRemoved}");
        }

        private static void PrintWarn(TruthHealthReport report, int warnMb, Options options, string chatDir)
        {
            if (report.Bytes <= warnMb * 1024L * 1024L)
                return;

            var sizeMb = report.Bytes / (1024d * 1024d);
            Console.WriteLine(
                $"WARN CHAT {report.ChatId} Truth.log {sizeMb:0.##}MB exceeds {warnMb}MB; consider manual archival.");

            Console.WriteLine(
                $"      Example: TruthDoctor --root \"{options.Root}\" --chat \"{report.ChatId}\" --compact --compact-lines {options.CompactLines}");

            if (!Directory.Exists(chatDir))
                return;
        }

        private static void WriteCompactSnapshot(TruthHealthReport report, Options options, string chatDir, string truthPath)
        {
            if (!File.Exists(truthPath))
                return;

            var entries = new Queue<TruthEntry>(options.CompactLines > 0 ? options.CompactLines : 1);
            foreach (var entry in TruthReader.Read(truthPath, repairTailFirst: options.RepairTailFirst))
            {
                if (options.CompactLines > 0 && entries.Count == options.CompactLines)
                    entries.Dequeue();
                entries.Enqueue(entry);
            }

            Directory.CreateDirectory(chatDir);

            var compactPath = Path.Combine(chatDir, "Truth.compact.log");
            var metaPath = Path.Combine(chatDir, "Truth.compact.meta.json");

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using (var writer = new StreamWriter(compactPath, append: false, encoding))
            {
                writer.NewLine = "\r\n";
                foreach (var entry in entries)
                    writer.WriteLine($"{entry.Role}|{entry.Payload}");
            }

            var meta = new CompactMeta(
                report.ChatId,
                truthPath,
                report.Bytes,
                report.ParsedEntryCount,
                entries.Count,
                options.CompactLines,
                DateTime.UtcNow);

            var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaPath, json, encoding);
        }

        private static void WriteJsonReport(string path, string root, List<TruthHealthReport> reports)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            var payload = new
            {
                Root = root,
                GeneratedUtc = DateTime.UtcNow,
                Reports = reports
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static IEnumerable<string> ResolveChatIds(string root, string? chatId)
        {
            if (!string.IsNullOrWhiteSpace(chatId))
                return new[] { chatId };

            var chatsRoot = Path.Combine(root, "Memory", "Chats");
            if (!Directory.Exists(chatsRoot))
                return Array.Empty<string>();

            return Directory.EnumerateDirectories(chatsRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => File.Exists(Path.Combine(chatsRoot, name!, TruthStorage.TruthFileName)))
                .Cast<string>()
                .ToArray();
        }

        private static bool TryParseArgs(string[] args, out Options options, out string? error)
        {
            error = null;
            var root = string.Empty;
            string? chatId = null;
            bool health = false;
            string? jsonPath = null;
            bool compact = false;
            int compactLines = 5000;
            int warnMb = 50;
            bool repairTailFirst = true;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--root":
                        if (!TryReadValue(args, ref i, out root))
                        {
                            error = "--root requires a path.";
                            options = default!;
                            return false;
                        }
                        break;
                    case "--chat":
                        if (!TryReadValue(args, ref i, out chatId))
                        {
                            error = "--chat requires a chat id.";
                            options = default!;
                            return false;
                        }
                        break;
                    case "--health":
                        health = true;
                        break;
                    case "--json":
                        if (!TryReadValue(args, ref i, out jsonPath))
                        {
                            error = "--json requires a path.";
                            options = default!;
                            return false;
                        }
                        break;
                    case "--compact":
                        compact = true;
                        break;
                    case "--compact-lines":
                        if (!TryReadValue(args, ref i, out var compactValue) || !int.TryParse(compactValue, out compactLines))
                        {
                            error = "--compact-lines requires an integer.";
                            options = default!;
                            return false;
                        }
                        if (compactLines <= 0)
                        {
                            error = "--compact-lines must be greater than 0.";
                            options = default!;
                            return false;
                        }
                        break;
                    case "--warn-mb":
                        if (!TryReadValue(args, ref i, out var warnValue) || !int.TryParse(warnValue, out warnMb))
                        {
                            error = "--warn-mb requires an integer.";
                            options = default!;
                            return false;
                        }
                        break;
                    case "--no-repair":
                        repairTailFirst = false;
                        break;
                    default:
                        error = $"Unknown argument: {arg}";
                        options = default!;
                        return false;
                }
            }

            if (string.IsNullOrWhiteSpace(root))
                root = ResolveProductRoot();

            options = new Options(root, chatId, health, jsonPath, compact, compactLines, warnMb, repairTailFirst);
            return true;
        }

        private static bool TryReadValue(string[] args, ref int index, out string value)
        {
            value = string.Empty;
            if (index + 1 >= args.Length)
                return false;
            value = args[++index];
            return true;
        }

        private static string ResolveProductRoot()
        {
            string bundleDir;
            try
            {
                var processPath = Environment.ProcessPath;
                bundleDir = !string.IsNullOrWhiteSpace(processPath)
                    ? (Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory)
                    : AppContext.BaseDirectory;
            }
            catch
            {
                bundleDir = AppContext.BaseDirectory;
            }

            if (Directory.Exists(Path.Combine(bundleDir, "Modules")) || Directory.Exists(Path.Combine(bundleDir, "Dock")))
                return bundleDir;

            var productDir = Path.Combine(bundleDir, "PRODUCT");
            if (Directory.Exists(Path.Combine(productDir, "Modules")) || Directory.Exists(Path.Combine(productDir, "Dock")))
                return productDir;

            var mainDir = Path.Combine(bundleDir, "MAIN");
            if (Directory.Exists(Path.Combine(mainDir, "Modules")))
            {
                var devProduct = Path.Combine(mainDir, "PRODUCT");
                return Directory.Exists(devProduct) ? devProduct : bundleDir;
            }

            return bundleDir;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("TruthDoctor --health [--chat <id>] [--root <path>] [--json <path>] [--compact] [--compact-lines <n>] [--warn-mb <n>] [--no-repair]");
        }
    }
}
