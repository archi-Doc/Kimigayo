// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

[assembly: AssemblyFixture(typeof(XunitTest.MirDifferenceLedger))]

namespace XunitTest;

/// <summary>The MIR difference ledger (docs/dev/DECISIONS.md R4-M17; R4–R6). With <c>KIMI_MIR_DIFF</c> naming a directory, the test
/// assembly observes every analysis (<see cref="Compilation.Analyzed"/>) and, once per mir-pipeline compilation and Binding pass, matches
/// the old path's A-owned records (<see cref="LegacyRecords"/>, the compared set of R4-M4) with the MIR's construction records, validator
/// findings and problems on code and subject node (<see cref="Sides"/>), appending one row per record to <c>raw-&lt;pid&gt;.tsv</c> there and saving the source once as
/// <c>sources/&lt;sha&gt;.kimi</c>. Disposal aggregates the rows (<see cref="Aggregate"/>). Without the variable nothing is observed.
/// </summary>
/// <remarks>A raw row is <c>event section side analysis code requirement location form body sha</c>: the comparison, complete (Binding
/// accepted the program: a complete result without a lint Error) or incomplete Binding (R4-M5), both, old or new, the owning analysis (A1,
/// A3, A4, A5, U for an old Unsupported gate, - for the expansion limit, B for a MIR construction record, validator finding or contract
/// break; a MIR problem takes its pair's in the compared set), the code, the requirement (the old record's; a MIR problem's own, R4-M67; -
/// for a MIR construction record or validator finding, which name none until H3; <c>old&gt;mir</c> for a matched pair whose requirements
/// differ), the subject's document, offset and length, its syntax form, the location of its enclosing definition and the source's
/// hash.</remarks>
public sealed partial class MirDifferenceLedger : IDisposable
{
    internal const int Event = 0;
    internal const int Section = 1;
    internal const int Side = 2;
    internal const int Analysis = 3;
    internal const int Code = 4;
    internal const int Requirement = 5;
    internal const int Location = 6;
    internal const int Form = 7;
    internal const int Body = 8;
    internal const int Sha = 9;

    private readonly string? directory = Environment.GetEnvironmentVariable("KIMI_MIR_DIFF") is { Length: > 0 } value ? value : null;
    private readonly ConditionalWeakTable<Compilation, StrongBox<ulong>> versions = new(); // The Binding pass each compilation was compared at.
    private readonly HashSet<string> sources = new(StringComparer.Ordinal); // The sources this process saved; also the lock of the files.
    private readonly StreamWriter? raw;
    private int events;

    public MirDifferenceLedger()
    {
        if (this.directory is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(this.directory, "sources"));
        this.raw = new(Path.Combine(this.directory, $"raw-{Environment.ProcessId}.tsv"), append: true, new UTF8Encoding(false)) { AutoFlush = true };
        Compilation.Analyzed += this.Record;
    }

    public void Dispose()
    {
        if (this.raw is null)
        {
            return;
        }

        Compilation.Analyzed -= this.Record;
        lock (this.sources)
        {
            this.raw.Dispose();
        }

        Aggregate(this.directory!);
    }

    /// <summary>Gets whether a compilation is Binding-complete (R4-M47): Binding accepted its program, a complete result and no lint
    /// Error, which leaves it complete (R5-M31).</summary>
    /// <param name="compilation">The bound compilation.</param>
    /// <returns>Whether it is Binding-complete.</returns>
    internal static bool BindingComplete(Compilation compilation)
        => compilation.Binding.Result.IsComplete && compilation.Binding.LintErrors == 0;

    // The definition whose body holds a node: the node itself or its nearest function, Property or accessor, else its tree's root.
    private static Koto BodyOf(Koto node)
    {
        while (node is not (FunctionKoto or PropertyKoto or PropertyAccessorKoto) && node.Parent is { } parent)
        {
            node = parent;
        }

        return node;
    }

    private static string Source(Compilation compilation)
    {
        var text = new StringBuilder();
        foreach (var module in compilation.SourceModules)
        {
            foreach (var document in module.SourceDocuments)
            {
                text.Append("// ").Append(Path.GetFileName(document.Path)).Append('\n').Append(document.SourceText).Append('\n');
            }
        }

        return text.ToString();
    }

    // Compares a mir-pipeline compilation once per Binding pass; the observer reads only the compilation's final records.
    private void Record(Compilation compilation)
    {
        if (compilation.Pipeline != CompilerPipeline.Mir)
        {
            return;
        }

        var program = compilation.BuildMir();
        if (Interlocked.Exchange(ref this.versions.GetOrCreateValue(compilation).Value, program.Version) == program.Version)
        {
            return;
        }

        var (_, old, _, mir) = Sides(compilation, program);
        if (old.Count + mir.Count != 0)
        {
            this.Write(compilation, Match(old, mir));
        }
    }

    private void Write(Compilation compilation, List<(string Side, Entry Entry)> matched)
    {
        var source = Source(compilation);
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12];
        var section = BindingComplete(compilation) ? "complete" : "incomplete";
        var comparison = $"{Environment.ProcessId}.{Interlocked.Increment(ref this.events)}";
        var rows = new StringBuilder();
        foreach (var (side, (code, requirement, analysis, node)) in matched)
        {
            rows.Append($"{comparison}\t{section}\t{side}\t{analysis}\t{code}\t{requirement}\t{LegacyRecords.Location(node)}+{node.Span.Length}\t")
                .Append($"{node.GetType().Name}\t{LegacyRecords.Location(BodyOf(node))}\t{sha}\n");
        }

        lock (this.sources)
        {
            this.raw!.Write(rows);
            if (this.sources.Add(sha))
            {
                File.WriteAllText(Path.Combine(this.directory!, "sources", sha + ".kimi"), source, new UTF8Encoding(false));
            }
        }
    }
}
