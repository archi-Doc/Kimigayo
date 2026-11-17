// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// PLAN G949 S1-1 (the library pass once per compilation; G949\s11\plan.md, section 3): what the library pass makes for S1-1's freeze. From
// S1-1 the library pass runs once and every later Bind is a program pass, so a fact a library object holds is made by the library pass
// itself, never on demand by a program pass; these rows read the library's facts after the empty program's Final Bind.
public partial class KimiLibraryShapeTest
{
    // k3 (E4's tripwire; F's ruling 167, Q3): no library conformance path has a standard Property witness (SPEC 11.9.2: a stored Field
    // witnessing a Property requirement, which a bridge accessor executes), so no program can make a witness bridge on a library path
    // (PropertyWitnessFunction makes one per path and witness) and S1-1 needs no eager bridge step: every library path's Property
    // witnesses are accessor calls (95 verified paths today). The unit that gives a library Type the first standard witness removes this
    // Fact and adds the eager step over the base paths, returning a library bridge without its refresh (s11-design section 4, E4).
    [Fact]
    public void TheLibraryDeclaresNoStandardPropertyWitness()
    {
        var c = Bound(string.Empty);
        var paths = DeclaredSymbols(c)
            .Where(x => x.Kind == BindingSymbolKind.Type && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha))
            .SelectMany(x => c.Binding.Semantics.Conformances(x) ?? [])
            .SelectMany(static x => x.Paths)
            .ToArray();
        Assert.NotEmpty(paths);
        Assert.Empty(paths.SelectMany(static x => x.PropertyWitnesses.Where(static w => w.Kind != PropertyWitnessKind.AccessorCall).Select(w => $"{x.Type.Name} is {x.Contract.Name}: {w.Kind}")));
    }

    // DeclQuery's Frozen column (B3; s11-design section 5, D-721's line on BU-105): a declaration query state stamped Frozen is the library
    // pass's in every pass, and a program pass's demand writes none of it: Running and NotStarted leave it (a not-Done SignatureVerdict
    // is then formed at each use, PLAN G375), and a completion is a broken contract. The stamp and states are set here by reflection on
    // one library function (from B4 the freeze writes them; e2 reads the per-use path through a program). Before B3 the Running write
    // took the frozen state (Current reads a Frozen stamp as the pass's).
    [Fact]
    public void AFrozenQueryStateIsNeverWritten()
    {
        var c = Bound(string.Empty);
        var function = DeclaredSymbols(c).First(x => x.Kind == BindingSymbolKind.Function && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha));
        var stamp = typeof(BindingSymbol).GetField("queryPass", BindingFlags.Instance | BindingFlags.NonPublic)!;
        stamp.SetValue(function, Binding.Frozen32);
        typeof(BindingSymbol).GetField("queries", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(function, (ushort)((int)DeclState.Done << ((int)DeclQueryKind.Signature * 2)));
        var pass = (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)! + 1;
        function.SetQueryState(DeclQueryKind.SignatureVerdict, pass, DeclState.Running);
        function.SetQueryState(DeclQueryKind.Signature, pass, DeclState.NotStarted);
        var refused = Assert.Throws<InvalidOperationException>(() => function.SetQueryState(DeclQueryKind.SignatureVerdict, pass, DeclState.Done));
        Assert.Contains("(InternalInvariant; PLAN G949 S1-1)", refused.Message, StringComparison.Ordinal);
        Assert.Equal(Binding.Frozen32, stamp.GetValue(function));
        Assert.Equal(DeclState.NotStarted, function.QueryState(DeclQueryKind.SignatureVerdict, pass));
        Assert.Equal(DeclState.Done, function.QueryState(DeclQueryKind.Signature, pass));
    }

    // e7 (s11 plan finding F2): the freeze stamps both counter families of the library's columns, so an edit, which advances the edit
    // counter, keeps the library's edit family (AssociatedOrigins: an associated Type declaration's Origin parameters): its rows are
    // the same objects right after the edit and after the next Bind. Before B4 the edit left the column stale (no rows) until the next
    // run's library pass rewrote it; a freeze that skipped the edit family would leave it stale for good.
    [Fact]
    public void AnEditKeepsTheLibrarysAssociatedOrigins()
    {
        var c = Bound(string.Empty);
        var rows = Rows(c);
        Assert.NotEmpty(rows);
        c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
        Assert.Equal(rows, Rows(c), ReferenceEqualityComparer.Instance);
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(rows, Rows(c), ReferenceEqualityComparer.Instance);

        static object[] Rows(Compilation c)
            => [.. LibraryLayer.Families().Where(static x => x.Name == "AssociatedOrigins").SelectMany(x => LibraryLayer.RowObjects(x.GetValue(c.Library.Kotonoha.Syntax.Hir.Families)!, c.Binding))];
    }

    // e2 (s11-design 5, 6.3; G375; G949 ruling 187 (c)): a frozen library verdict is read, never written, so a program's per-use check
    // over a library function leaves the frozen verdict and the records as the library pass made them; one the freeze left not Done is
    // formed at each use in the user's scope. None is today (EveryLibrarySignatureVerdictIsProven), so the row clears every library
    // function's SignatureVerdict by reflection after the first Bind (the stamps stay Frozen): a second Bind of Hello then forms the
    // verdicts its uses demand (more signature walks than a second Bind without the clearing), keeps its Result and records, and leaves
    // every cleared state NotStarted and Frozen. Before B4 nothing was Frozen; with the freeze but without the per-use rule, the use's
    // formation proved the verdict and its completion threw the frozen state's internal invariant.
    [Fact]
    public void AFrozenVerdictThatIsNotDoneIsFormedAtEachUse()
    {
        var (walks, result, records) = Second(clear: false);
        var (cleared, clearedResult, clearedRecords) = Second(clear: true);
        Assert.True(cleared > walks, $"{cleared} signature walks with the verdicts cleared, {walks} without");
        Assert.Equal(result, clearedResult);
        Assert.Equal(records, clearedRecords);

        static (int Walks, BindingResult Result, string[] Records) Second(bool clear)
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText("docs/examples/Hello/Hello.kimi".Split('/'))));
            Assert.True(c.Bind().IsComplete);
            var queries = typeof(BindingSymbol).GetField("queries", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var functions = DeclaredSymbols(c).Where(x => x.Kind == BindingSymbolKind.Function && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha)).ToArray();
            Assert.All(functions, static x => Assert.True(x.QueriesFrozen));
            foreach (var function in clear ? functions : [])
            {
                queries.SetValue(function, (ushort)((ushort)queries.GetValue(function)! & ~(3 << ((int)DeclQueryKind.SignatureVerdict * 2))));
            }

            var result = c.Bind();
            var pass = (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
            Assert.All(functions, x => Assert.True(x.QueriesFrozen && (x.QueryState(DeclQueryKind.SignatureVerdict, pass) == DeclState.NotStarted) == clear));
            var walks = (int)typeof(Binding).GetField("signatureWalks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
            return (walks, result, [.. TestDiagnostics.Of(c).Select(static x => $"{x.Code} {x.Span}")]);
        }
    }

    // F-1 and F-3 (s11-design 2.3 and 6.3, RC4, RC7; G949 ruling 167): the library's freeze verifies its query states and refuses a
    // library with records, so on each target class the library rows cover (probe 1: pointer widths 64 with the default
    // x86_64-pc-windows-msvc, 32 and 16; eight triples gave no record and identical library MIR) the empty program's Final Bind
    // completes with no record of any kind, warnings included, and every library header DeclQuery binds and every library struct's
    // inherited conformances are Done and Frozen.
    [Theory]
    [InlineData("x86_64-pc-windows-msvc")]
    [InlineData("i686-unknown-linux-gnu")]
    [InlineData("avr-unknown-unknown")]
    public void TheLibraryPassCompletesEveryQuery(string target)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", "public func main() => ()\n"));
        Assert.True(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c).Select(static x => $"{x.Severity} {x.Code} {x.Path} {x.Span}"));
        var pass = (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        var library = DeclaredSymbols(c).Where(x => ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha)).ToArray();
        Assert.Empty(library.Where(x => !x.QueriesFrozen).Select(static x => x.Name));
        Assert.Empty(library.Where(x => x.Kind is BindingSymbolKind.Function or BindingSymbolKind.Property && x.Declaration is FunctionKoto or PropertyKoto
            && x.QueryState(DeclQueryKind.Signature, pass) != DeclState.Done).Select(static x => x.Name));
        Assert.Empty(library.Where(x => x.Declaration is StructKoto && x.QueryState(DeclQueryKind.InheritedConformances, pass) != DeclState.Done).Select(static x => x.Name));
    }

    // k4 (B1: E3's effect bounds, E7, R): the library pass completes the facts a later pass would otherwise form lazily on a library
    // object, so a program pass reads them and writes none: every bound Contract reference of the layer's part has its effect bounds
    // (EffectState 2), every library scope, and every scope of a library path (its own, its requirements' premises, its overlaps'),
    // denotes its current view (SerialOf would write none), and every local region of the layer's part has its sources formed. Before B1,
    // after the empty program's Bind, 4 of 5 bound references had no effect bounds, 595 of 1,187 library scopes and 123 of 171 path
    // scopes a view SerialOf would write, and all 19 regions had no sources (Version -1).
    [Fact]
    public void TheLibraryPassLeavesNoLazyFactOnTheLibrary()
    {
        var c = Bound(string.Empty);
        var layer = c.Library.Layer.Part;
        Assert.NotEmpty(layer.BoundContracts);
        Assert.Empty(layer.BoundContracts.Values.Where(static x => x.Contract!.EffectState != 2).Select(static x => x.Type!.ToString()));
        var scopes = new List<BindingScope>();
        foreach (var scope in c.Library.Kotonoha.Syntax.Hir.Families.Scopes.Enumerate())
        {
            if (scope is not null)
            {
                scopes.Add(scope);
            }
        }

        foreach (var path in DeclaredSymbols(c)
            .Where(x => x.Kind == BindingSymbolKind.Type && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha))
            .SelectMany(x => c.Binding.Semantics.Conformances(x) ?? []).SelectMany(static x => x.Paths))
        {
            scopes.Add(path.Scope);
            scopes.AddRange((path.WitnessScopes ?? []).Select(static x => x.Premises));
            scopes.AddRange((path.OverlapScopes ?? []).Select(static x => x.Scope));
        }

        Assert.Empty(scopes.Where(static x => State(x) != x.ViewState).Select(static x => x.Owner?.ToString()));
        Assert.NotEmpty(layer.Regions);
        Assert.Empty(layer.Regions.Where(static x => x.Value.Version == -1).Select(static x => x.Key.ToString()));

        // The chain state SerialOf compares with a scope's ViewState, read without writing (ContractSolver.SerialOf).
        static (int, int, int) State(BindingScope scope) => (scope.Constraints?.Version ?? 0, scope.LinkVersion, scope.Parent is { } parent ? Serial(parent) : 0);

        static int Serial(BindingScope scope) => State(scope) != scope.ViewState ? scope.ViewSerial + 1 : scope.ViewSerial;
    }
}
