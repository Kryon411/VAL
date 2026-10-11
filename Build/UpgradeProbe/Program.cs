using System.Reflection;

if (args.Length != 3)
    throw new ArgumentException("Expected Truth assembly, isolated archive root, and seed/upgrade/rollback/verify mode.");

var assemblyPath = Path.GetFullPath(args[0]);
var archiveRoot = Path.GetFullPath(args[1]);
if (!Path.IsPathFullyQualified(args[1]))
    throw new ArgumentException("An explicit absolute synthetic archive root is required.");

var assembly = Assembly.LoadFrom(assemblyPath);
var publisherType = assembly.GetType("VAL.Continuum.Pipeline.Truth.NullTruthTelemetryPublisher", throwOnError: true)!;
var publisher = publisherType.GetProperty("Instance")!.GetValue(null)!;
var storeType = assembly.GetType("VAL.Continuum.Pipeline.Truth.TruthStore", throwOnError: true)!;
var store = Activator.CreateInstance(storeType, publisher, archiveRoot)!;
const string chatId = "synthetic-upgrade-fixture";
const string user = "Synthetic archive fixture - A1\nSecond line with | separators and caf\u00e9.";
const string assistant = "Synthetic response - B1.";
const string upgraded = "Synthetic response written by the candidate - C1.";
const string rolledBack = "Synthetic response written after rollback - D1.";
var initial = new[] { "U|" + user.Replace("\n", "\\n", StringComparison.Ordinal), "A|" + assistant };

try
{
    switch (args[2])
    {
        case "seed":
            Require(Append('U', user) && Append('A', assistant), "Legacy writes must succeed.");
            Require(!Append('U', user), "Legacy duplicate suppression must work.");
            AssertLines(initial);
            break;
        case "upgrade":
            AssertLines(initial);
            Require(!Append('U', user), "The candidate must recognize legacy duplicates.");
            object?[] beginArgs = [chatId, true, null, null, CancellationToken.None];
            Require((bool)Invoke("TryBeginTruthRebuild", beginArgs)!, "Candidate rebuild must start.");
            var backup = (string)beginArgs[2]!;
            Require(File.ReadAllLines(backup).SequenceEqual(initial), "The rebuild must retain an exact legacy backup.");
            Require(Append('U', user) && Append('A', assistant) && Append('A', upgraded), "Candidate rebuild writes must succeed.");
            Require((bool)Invoke("TryCommitTruthRebuild", chatId)!, "Candidate rebuild must commit.");
            AssertLines([.. initial, "A|" + upgraded]);
            break;
        case "rollback":
            AssertLines([.. initial, "A|" + upgraded]);
            Require(!Append('A', upgraded), "Legacy code must recognize candidate duplicates.");
            Require(Append('A', rolledBack), "Legacy code must append to the candidate archive.");
            AssertLines([.. initial, "A|" + upgraded, "A|" + rolledBack]);
            break;
        case "verify":
            AssertLines([.. initial, "A|" + upgraded, "A|" + rolledBack]);
            Require(!Append('A', rolledBack), "The candidate must recognize rollback duplicates.");
            break;
        default:
            throw new ArgumentException("Unknown compatibility probe mode.");
    }
    Console.WriteLine($"PASS: {args[2]} using {assembly.GetName().Version} against synthetic archives.");
}
finally
{
    (store as IDisposable)?.Dispose();
}

object? Invoke(string method, params object?[] arguments) => storeType.GetMethod(method)!.Invoke(store, arguments);
bool Append(char role, string text) => (bool)Invoke("AppendTruthLine", chatId, role, text)!;
void AssertLines(string[] expected) => Require(((string[])Invoke("ReadTruthLines", chatId)!).SequenceEqual(expected), "Archive content changed unexpectedly.");
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
