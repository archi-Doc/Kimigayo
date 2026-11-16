// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
using System.Reflection;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;
using Printer = XunitTest.SemanticSnapshotTest.SemanticPrinter;

namespace XunitTest;

// PLAN G949 Stage 1 (the design's coverage rows c13 and c17): the library pass binds the Kimi library once per compilation, and every
// later Bind is a program pass over the source modules. c13 fails at dev a63b1598b and is Explicit until G949 S1-1 lands the library
// pass; c17 passes since S1-e (S1-1's SyntaxTable refusal lands only after it), its corpus row Explicit as the library-layer corpus row is.
public partial class KimiLibraryShapeTest
{
    // c13 (bound once): after the first Bind, a second Bind's node table holds the program's nodes only, while its Result keeps the
    // library layer's counts (design R1.1, c15). S1-1 adds the library's publication version (R1.3), unchanged by the second pass.
    [Fact(Explicit = true)] // Until G949 S1-1.
    public void ASecondBindBindsNoLibraryNode()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText("docs/examples/Hello/Hello.kimi".Split('/'))));
        var first = c.Bind();
        Assert.True(first.IsComplete);
        var second = c.Bind();
        Assert.Equal(first, second);
        Assert.DoesNotContain(c.Binding.Semantics.BoundNodes, x => ReferenceEquals(x.Kotonoha, c.Library.Kotonoha));
    }

    // The empty program's synthesized library nodes after its first Analyze, by id (c17's reference).
    private static readonly Lazy<string[]> EmptyProgramSynthesized = new(() => SynthesizedLines(Analyzed(string.Empty)));

    // c17 (RC1): the library steps number every library node a later phase reads, so the library table after the first Analyze is the
    // one after the first Bind, and its synthesized nodes are the empty program's, id by id, whatever the program (R8-M32's Temporary 1
    // removed). Until G949 S1-e the MIR builder's CallPlan.Accessor created and numbered 7 library accessors' execution functions
    // (Binding.AccessorFunction) after Bind, 16,120 against 16,134, and a program's uses ordered the ids (Milestones 27 and 30). c2's
    // programs and every milestone here; the library-layer rows' whole corpus (c16's) in the corpus row, valid or not: a check analyzes
    // whatever Binding found (SPEC 23.1.2).
    [Theory]
    [MemberData(nameof(LayerPrograms))]
    public void TheLibraryPassNumbersEveryLibraryNode(string program) => NumberedByTheLibraryPass(program);

    [Theory(Explicit = true)] // Run with the library-layer corpus row (PLAN G949 S1-0b).
    [MemberData(nameof(LayerCorpus))]
    public void TheLibraryPassNumbersEveryLibraryNodeOfTheCorpus(string program) => NumberedByTheLibraryPass(program);

    // G949 S1-e: after a Final Bind every library function's SignatureVerdict is Proven, kept as Done (358 today), so a program's use
    // computes none and BU-105's Frozen line needs no clause for a library verdict that is not Proven. The domain is Binding's symbols,
    // not the step's walk.
    [Fact]
    public void EveryLibrarySignatureVerdictIsProven()
    {
        var c = Bound(string.Empty);
        Assert.Equal(BindingMode.Final, c.Binding.Result.Mode);
        var pass = (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        var symbols = (IDictionary)typeof(Binding).GetField("symbols", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        var functions = symbols.Values.Cast<BindingSymbol>()
            .Where(x => x.Kind == BindingSymbolKind.Function && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha)).ToArray();
        Assert.NotEmpty(functions);
        Assert.Empty(functions.Where(x => x.QueryState(DeclQueryKind.SignatureVerdict, pass) != DeclState.Done).Select(static x => Line(x.Declaration)));
    }

    // PLAN G949 S1-r1a (review-r1a B2): CollectCallableEffectUses tests every pass's bounded Callable clauses before it collects a pass's
    // uses, since a use may select a library callee's clause (SPEC 8.11.4). No program can show that read until the library declares
    // such a clause: today every clause a use selects is the program's, in the pass's own part. The unit whose library declares the
    // first one removes this Fact and adds the cross-part row: a program stating no bounded clause of its own calls that library
    // function, and its use gets its effect obligation (S1-r1b's c15 extension, ruling 65, once the parts split).
    [Fact]
    public void TheLibraryDeclaresNoBoundedCallableClause()
        => Assert.Empty(KotoTree.Walk(Bound(string.Empty).Library.Kotonoha.RootKoto).OfType<IsKoto>().Where(static x => x.EffectBounds.Count != 0).Select(static x => Line(x)));

    private static Compilation Analyzed(string program)
    {
        var c = Bound(program);
        c.Analyze();
        return c;
    }

    private static void NumberedByTheLibraryPass(string program)
    {
        var c = Bound(program);
        var bound = c.Library.Kotonoha.Syntax.Count;
        c.Analyze();
        Assert.Equal(bound, c.Library.Kotonoha.Syntax.Count);
        Assert.Equal(EmptyProgramSynthesized.Value, SynthesizedLines(c));
    }

    // The library table's synthesized nodes in id order, each by its printer line.
    private static string[] SynthesizedLines(Compilation c)
    {
        var table = c.Library.Kotonoha.Syntax;
        var lines = new string[table.Count - table.ParsedCount];
        for (var i = 0; i < lines.Length; i++)
        {
            var printer = new Printer(false);
            printer.Node(table.Nodes[table.ParsedCount + 1 + i]!);
            lines[i] = $"{table.ParsedCount + 1 + i} {printer.ToString().TrimEnd('\n')}";
        }

        return lines;
    }
}
