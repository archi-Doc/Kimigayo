using RecordsModel;

if (args.Length != 1) throw new ArgumentException("Supply a new evidence output directory.");
var output = Path.GetFullPath(args[0]);
if (Directory.Exists(output)) throw new ArgumentException("Preserve prior runs: output directory must be new.");
Directory.CreateDirectory(output);
var expected = Expected.All(); // Handwritten oracle, emitted before executing the protocol.
File.WriteAllLines(Path.Combine(output, "expected.txt"), expected.OrderBy(p => p.Key, StringComparer.Ordinal)
    .SelectMany(p => new[] { $"CASE {p.Key}" }.Concat(p.Value)));
using var actualLog = new StreamWriter(Path.Combine(output, "actual.txt"));
using var summary = new StreamWriter(Path.Combine(output, "summary.txt")) { AutoFlush = true };
using var negatives = new StreamWriter(Path.Combine(output, "negative-controls.txt"));
using var mismatches = new StreamWriter(Path.Combine(output, "mismatches.txt"));
var failures = 0;
var permutations = 0;
var scenarios = new Fixtures().All().ToArray();
if (!expected.Keys.Order().SequenceEqual(scenarios.Select(c => c.Name).Order())) throw new Exception("Oracle/scenario inventory mismatch");
Report Run(Scenario scenario, IEnumerable<Input> rows)
{
    var report = new Report(); report.Begin(scenario.Domain);
    foreach (var row in rows) report.Record(row);
    report.Complete(); return report;
}
bool Equal(string[] want, string[] got) => want.SequenceEqual(got, StringComparer.Ordinal);
foreach (var scenario in scenarios)
{
    var count = 0;
    var caseFailures = 0;
    foreach (var order in Permutations(Enumerable.Range(0, scenario.Rows.Length).ToArray()))
    {
        var report = Run(scenario, order.Select(i => scenario.Rows[i]));
        var actual = Format.Snapshot(report);
        actualLog.WriteLine($"CASE {scenario.Name} PERMUTATION {string.Join(',', order)}");
        foreach (var line in actual) actualLog.WriteLine(line);
        count++; permutations++;
        if (!Equal(expected[scenario.Name], actual))
        {
            if (caseFailures == 0)
            {
                mismatches.WriteLine($"CASE {scenario.Name} first mismatch permutation {string.Join(',', order)}");
                mismatches.WriteLine("EXPECTED"); foreach (var line in expected[scenario.Name]) mismatches.WriteLine(line);
                mismatches.WriteLine("ACTUAL"); foreach (var line in actual) mismatches.WriteLine(line);
            }
            failures++; caseFailures++;
        }
    }
    summary.WriteLine($"{scenario.Name}: permutations={count}; complete-output-mismatches={caseFailures}; {scenario.Meaning}");
}
foreach (var (name, caseName) in new[] { ("first-facts", "duplicates"), ("span", "default-chain"), ("Loan", "return-exact"), ("removed-preferred", "default-chain") })
{
    var scenario = scenarios.Single(c => c.Name == caseName);
    var mutated = name switch
    {
        "first-facts" => Run(scenario, Ablations.FirstFacts(scenario)).All,
        "span" => Run(scenario, Ablations.Span(scenario)).All,
        "Loan" => Run(scenario, Ablations.Loan(scenario)).All,
        "removed-preferred" => Ablations.RemovedPreferred(scenario, Run(scenario, scenario.Rows).All),
        _ => throw new Exception("Unlisted ablation"),
    };
    var actual = mutated.Select(Format.Row).Order(StringComparer.Ordinal)
        .Concat(Report.Publish(mutated, scenario.Domain).Select(Format.Publication)).ToArray();
    negatives.WriteLine($"CONTROL {name} CASE {caseName}");
    negatives.WriteLine("EXPECTED"); foreach (var line in expected[caseName]) negatives.WriteLine(line);
    negatives.WriteLine("ACTUAL"); foreach (var line in actual) negatives.WriteLine(line);
    var detected = !Equal(expected[caseName], actual);
    negatives.WriteLine($"MISMATCH REQUIRED: {detected}");
    summary.WriteLine($"negative-control {name}: detected={detected}");
    if (!detected) failures++;
}
try
{
    var checks = ProtocolChecks.Run();
    File.WriteAllLines(Path.Combine(output, "protocol-checks.txt"), checks.Lines);
    summary.WriteLine($"protocol-checks={checks.Lines.Length}; mismatches={checks.Failures}");
    failures += checks.Failures;
}
catch (Exception error)
{
    File.WriteAllText(Path.Combine(output, "protocol-failure.txt"), error.ToString()); failures++;
}
summary.WriteLine($"scenarios={scenarios.Length}; permutations={permutations}; required-negative-controls=4; failures={failures}");
Console.WriteLine($"scenarios={scenarios.Length}; permutations={permutations}; failures={failures}; evidence={output}");
return failures == 0 ? 0 : 1;

static IEnumerable<int[]> Permutations(int[] values)
{
    if (values.Length == 0) { yield return []; yield break; }
    for (var i = 0; i < values.Length; i++)
        foreach (var tail in Permutations(values.Where((_, j) => j != i).ToArray()))
            yield return [values[i], .. tail];
}
