// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// Throwaway probe of round 2x-P7 (not committed): with KIMI_P7_DIR naming a folder of .kimi cases, binds each as an Application under the
// mir pipeline, analyzes it and writes results.txt there: each Closure's receiver and signature, the default pipeline's Error and Warning
// records (Binding's and the old analyses'), and the MIR's (Binding's and the MIR's records, findings and problems, MirDifferenceLedger.Sides).
// With KIMI_P7_DUMP set, also writes <case>.mir with the module's MIR.
public class ScratchProbeP7Test
{
    [Fact]
    public void Run()
    {
        var dir = Environment.GetEnvironmentVariable("KIMI_P7_DIR");
        if (dir is null)
        {
            return;
        }

        var dump = Environment.GetEnvironmentVariable("KIMI_P7_DUMP") is { Length: > 0 };
        var text = new StringBuilder();
        foreach (var file in Directory.GetFiles(dir, "*.kimi").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var source = File.ReadAllText(file).ReplaceLineEndings("\n");
            text.Append("=== ").Append(name).Append('\n');
            try
            {
                var c = CompilationTestHelper.Parse(source, "Hello.kimi");
                c.Pipeline = CompilerPipeline.Mir;
                c.Bind();
                _ = c.Binding.CheckStartup(OutputKind.Application);
                foreach (var node in KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(static x => x.IsAnonymous))
                {
                    var closure = node.ClosureOf();
                    text.Append($"  closure L{Line(source, node.Span.Start)}: receiver {closure?.Receiver.ToString() ?? "-"}\n");
                }

                c.Analyze();
                var program = c.BuildMir();
                var (common, old, passed, mir) = MirDifferenceLedger.Sides(c, program);
                text.Append("  default:").Append(Rows(source, [.. common, .. old, .. passed])).Append('\n');
                text.Append("  mir:    ").Append(Rows(source, [.. common, .. mir])).Append('\n');
                var parse = TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error && x.Code.Contains("Syntax", StringComparison.Ordinal)).ToArray();
                if (parse.Length > 0)
                {
                    text.Append("  SYNTAX: ").Append(string.Join("; ", parse.Select(static x => x.Code))).Append('\n');
                }

                if (dump)
                {
                    var mirText = new StringBuilder();
                    MirDump.Write(program, 0, mirText);
                    File.WriteAllText(Path.Combine(dir, name + ".mir"), mirText.ToString());
                }
            }
            catch (Exception e)
            {
                text.Append("  EXCEPTION ").Append(e.GetType().Name).Append(": ").Append(e.Message.Split('\n')[0]).Append('\n');
            }
        }

        File.WriteAllText(Path.Combine(dir, "results.txt"), text.ToString());
    }

    private static string Rows(string source, IEnumerable<MirDifferenceLedger.Entry> entries)
    {
        var rows = entries.Where(static x => DiagnosticEntries.TryGet(x.Code, out var entry) && entry.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(x => x.Node.CodeContext?.SourceDocument?.Path == "Hello.kimi"
                ? $"{x.Code}@L{Line(source, x.Node.Span.Start)}`{Clip(source.Substring(x.Node.Span.Start, x.Node.Span.Length))}`"
                : $"{x.Code}@{LegacyRecords.Location(x.Node)}")
            .ToList();
        return rows.Count == 0 ? " accepted" : " " + string.Join(" | ", rows);
    }

    private static int Line(string source, int offset) => source.AsSpan(0, offset).Count('\n') + 1;

    private static string Clip(string text) => (text.Split('\n')[0] is var first && first.Length > 40 ? first[..40] + "…" : text.Split('\n')[0]).Trim();
}
