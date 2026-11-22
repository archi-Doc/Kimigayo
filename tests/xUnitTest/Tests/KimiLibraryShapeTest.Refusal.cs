// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// PLAN G949 Stage 2 U2a (design u2-design 2.4; review-u2 C1-C3 and its delta check; B's rulings 6, 15-17, 24, 33; the Front's C-380
// 16:55 and 17:08): a library pass with a record of any kind is refused as a value, never thrown. Its Binding-side records are kept as
// the layer's refusal while its tables stand and published as InternalInvariant_Kd at their library locations; a syntax record stays the
// library module's syntax list's, published under its own code by that list's owner (form (R)); the refused Bind leaves no result and
// runs no program pass, and the check publishes only the refusal and builds no MIR. The probe sources are labelled in
// back\notes\l1-s2\u2a-impl\labels.md (invalid by SPEC 6.7 and Appendix F's grammar).
public partial class KimiLibraryShapeTest
{
    private const string ZUnsupportedProbe = "#Unsupported\npublic struct ZProbe\n    public let x: i32\n\n    public struct Inner\n        public let y: i32\n";

    // s6r (C2): a refused layer holds only its refusal: no library node in the composed views, the library part reset, the table not
    // frozen; a second Bind publishes equal records. From PLAN G949 Stage 2 U3 (N3; changed by decision) the compilation reads the
    // library its freeze compilation refused (the seam) and runs no pass, so the second Bind reads the same refusal; Stage 1's row ran the
    // library pass again in the compilation that held it, and refused identically.
    [Fact]
    public void ARefusedLibraryLeavesOnlyItsRefusal()
    {
        var c = RefusedLibrary(ZUnsupportedProbe + "public group ZUses\n    public func zuse(v: ref/::ZProbe.Inner) => ()\n");
        var first = c.Library.Layer.Refusal!;
        Assert.Single(first.Records);
        Assert.Equal(first.Records[0], first.First); // The first counted record: no syntax record here.
        Assert.Empty(c.Binding.Issues);
        Assert.Empty(c.Library.Layer.Part.Issues);
        Assert.Empty(c.Library.Layer.Part.Nodes);
        Assert.False(c.Library.Kotonoha.Syntax.Frozen);
        Assert.Equal(default, c.Bind());
        Assert.Equal(first.Records, c.Library.Layer.Refusal!.Records);
    }

    // s6d (C3; SPEC 23.1.2): two independent library records are both in the refusal and both published, each at its own library location.
    [Fact]
    public void TwoLibraryRecordsAreBothPublishedAtTheirLocations()
    {
        var library = ZUnsupportedProbe + "public group ZUses\n    public func zuse(v: ref/::ZProbe.Inner) => ()\n    public func zuse2(v: ref/::ZProbe) => ()\n";
        var c = RefusedLibrary(library);
        Assert.Equal(2, c.Library.Layer.Refusal!.Records.Length);
        Assert.False(c.Check());
        var published = TestDiagnostics.Of(c);
        Assert.Equal(3, published.Length); // Each record, and the refusal's own at the first (ruling 40 (i)).
        Assert.All(published, x =>
        {
            Assert.Equal(nameof(Kimi.DiagnosticCode.InternalInvariant_Kd), x.Code);
            Assert.Equal(c.Library.Kotonoha.Name, x.Path); // The probe's library document.
        });
        SourceSpan[] uses = [new(library.IndexOf("::ZProbe.Inner", StringComparison.Ordinal) + 2, 6), new(library.IndexOf("::ZProbe)", StringComparison.Ordinal) + 2, 6)];
        Assert.Equal(uses, published.Select(static x => x.Span).Distinct().Order().ToArray());
    }

    // s6b and s6p (C1 (x); form (R)): a library whose only defect is a syntax error (here a parameter list without its `)`, which the parser
    // reports as three syntax records) is refused by the clause's syntax term; each parser record is published once under its own code, and
    // the refusal holds no copy of one (a Binding record derived from them is left out as Finalize leaves it out).
    [Fact]
    public void ASyntaxOnlyLibraryDefectIsRefusedAndItsRecordStaysTheParsers()
    {
        var library = "public group ZSyntax\n    public func f(x: i32 -> i32 => x\n";
        var c = RefusedLibrary(library);
        var refusal = c.Library.Layer.Refusal!;
        Assert.False(c.Check());
        var published = TestDiagnostics.Of(c);
        var parsers = published.Where(static x => x.Code is nameof(Kimi.DiagnosticCode.ExpectedSyntax_Kd) or nameof(Kimi.DiagnosticCode.MissingSyntax_Kd)).ToArray();
        Assert.Equal(3, parsers.Length);
        Assert.Equal(parsers.Length, parsers.Select(static x => (x.Code, x.Span)).Distinct().Count());
        Assert.Equal(library.Length - 1, Assert.Single(parsers, static x => x.Code == nameof(Kimi.DiagnosticCode.MissingSyntax_Kd)).Span.Start); // Missing `)`.
        // Beside them only InternalInvariant_Kd: the refusal's own at the first parser record (ruling 40 (i)) and any Binding-side record.
        var internals = published.Where(static x => x.Code == nameof(Kimi.DiagnosticCode.InternalInvariant_Kd)).ToArray();
        Assert.Equal(published.Length, parsers.Length + internals.Length);
        Assert.Contains(internals, x => x.Span == refusal.First.Span);
        Assert.Contains(parsers, x => x.Span == refusal.First.Span);
    }

    // Ruling 33 (F2): the check of a compilation over a refused library publishes only the refusal and builds no MIR.
    [Fact]
    public void ARefusedLibrarysCheckBuildsNoMir()
    {
        var c = RefusedLibrary(ZUnsupportedProbe + "public group ZUses\n    public func zuse(v: ref/::ZProbe.Inner) => ()\n");
        Assert.False(c.Check());
        Assert.Null(typeof(Compilation).GetField("mir", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c));
        Assert.All(TestDiagnostics.Of(c), static x => Assert.Equal(nameof(Kimi.DiagnosticCode.InternalInvariant_Kd), x.Code));
    }

    // P4 (the Front's C1; B's ruling 40 (i)): a library whose only record is a syntax Warning (a style rule; labelled in
    // back\notes\l1-s2\u2a-impl\labels.md) is refused, and its rejected check is explained: the Warning under its own code beside the
    // refusal's own InternalInvariant_Kd at its location. Before ruling 40 the check published the Warning alone, and finalizing the
    // rejected result threw UnexplainedRejection.
    [Fact]
    public void ALibraryWhoseOnlyRecordIsAWarningIsRefusedWithAnError()
    {
        var c = RefusedLibrary("public struct ZOrder\n    public func f() => ()\n    public let x: i32\n");
        Assert.Empty(c.Library.Layer.Refusal!.Records);
        Assert.False(c.Check());
        var published = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        var warning = Assert.Single(published, static x => x.Code == nameof(Kimi.DiagnosticCode.DeclarationOrderWarning_Kd));
        var error = Assert.Single(published, static x => x.Code == nameof(Kimi.DiagnosticCode.InternalInvariant_Kd));
        Assert.Equal(warning.Span, error.Span);
        Assert.Equal(2, published.Length);
    }

    // s6c2 and h2 (PLAN G949 Stage 2 U3; ruling 15): two compilations given one refused library each run no pass (the library table and
    // every level of its layer keep their counts: a refused library is never sealed, so an intern would write the shared layer), leave
    // no result, and publish the refusal in their own records, each record once, and the same again after a second Bind.
    [Fact]
    public void TwoCompilationsGivenOneRefusedLibraryEachPublishItsRefusal()
    {
        var freeze = Unfrozen(Kimi.Compiler.Target.TargetTriple.Parse(TargetProfile.Windows.Target), debugAndSetting: false);
        freeze.Library.Kotonoha.CreateCodeContext().Parse(freeze.Library.Kotonoha.RootKoto, ZUnsupportedProbe + "public group ZUses\n    public func zuse(v: ref/::ZProbe.Inner) => ()\n");
        var library = KimiLibrary.Freeze(freeze);
        var (counts, refusal) = (Counts(library), library.Layer.Refusal!);
        TestDiagnostic[]? records = null;
        foreach (var c in new[] { Reading(library), Reading(library) })
        {
            for (var bind = 0; bind < 2; bind++)
            {
                Assert.Equal(default, c.Bind());
                Assert.False(c.Check());
                var published = TestDiagnostics.Of(c);
                Assert.Equal(refusal.Records.Length + 1, published.Length); // Each record once, and the refusal's own at the first (ruling 40 (i)).
                Assert.All(published, static x => Assert.Equal(nameof(Kimi.DiagnosticCode.InternalInvariant_Kd), x.Code));
                Assert.Equal(records ??= published, published);
                Assert.Equal(counts, Counts(library));
            }
        }

        static Compilation Reading(KimiLibrary library)
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(TargetProfile.Windows.Target));
            c.UseLibrary(library);
            c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(v: ref/i32) => ()\n");
            return c;
        }

        static string Counts(KimiLibrary library)
        {
            var (layer, part) = (library.Layer, library.Layer.Part);
            return $"{library.Kotonoha.Syntax.Count} {layer.Types.Count} {layer.Substs.Count} {layer.Intersections.Count} {layer.Lengths.Count} " +
                $"{layer.Constraints.Count} {part.Nodes.Count} {part.Issues.Count} {part.DerivedIssues.Count} {part.Obligations.Count} {part.Regions.Count}";
        }
    }

    // s6p2 and s6p2b (PLAN G949 Stage 2 U3; ruling 40 (i); design R2.1, N8): a library refused for a syntax error in each of two documents
    // is read by two compilations. Each registers the frozen syntax list renumbered into its own source table: each parser record is
    // published once, under its own code, located in its own document, beside the refusal's own record; the documents are listed in the
    // freeze's order, and each holds its syntax errors in each compilation.
    [Fact]
    public void TheRefusedLibrarysSyntaxRecordsAreEachReadersOwn()
    {
        var freeze = Unfrozen(Kimi.Compiler.Target.TargetTriple.Parse(TargetProfile.Windows.Target), debugAndSetting: false);
        freeze.Library.Kotonoha.AddSource(new("ZA.kimi", "public group ZA\n    public func f(x: i32 -> i32 => x\n"));
        freeze.Library.Kotonoha.AddSource(new("ZB.kimi", "public group ZB\n    public func g(y: i32 -> i32 => y\n"));
        var library = KimiLibrary.Freeze(freeze);
        var refusal = library.Layer.Refusal!;
        string[]? records = null;
        for (var i = 0; i < 2; i++)
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(TargetProfile.Windows.Target));
            c.UseLibrary(library);
            c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(v: ref/i32) => ()\n");
            Assert.False(c.Check());
            var result = c.Diagnostics.Finalize(rejected: true);
            var parsers = result.Diagnostics.Where(static x => x.Code is nameof(Kimi.DiagnosticCode.ExpectedSyntax_Kd) or nameof(Kimi.DiagnosticCode.MissingSyntax_Kd)).ToArray();
            string[] paths = [.. parsers.Select(x => result.Sources[x.Source].Path).Distinct()];
            Assert.Equal(2, paths.Length);
            Assert.EndsWith("ZA.kimi", paths[0], StringComparison.Ordinal);
            Assert.EndsWith("ZB.kimi", paths[1], StringComparison.Ordinal);
            Assert.All(parsers, x => Assert.All(x.Related ?? [], r => Assert.True(r.Source < 0 || r.Source == x.Source)));
            Assert.Single(result.Diagnostics, x => x.Code == nameof(Kimi.DiagnosticCode.InternalInvariant_Kd) && x.Span == refusal.First.Span);
            Assert.True(c.Diagnostics.HasSyntaxErrors(refusal.First.Document!));
            string[] described = [.. result.Diagnostics.Select(x => $"{x.Code} {(x.Source < 0 ? "-" : result.Sources[x.Source].Path)} {x.Span} [{string.Join(", ", (x.Related ?? []).Select(static r => $"{r.Role} {r.Source} {r.Span}"))}]")];
            Assert.Equal(described.Length, described.Distinct().Count());
            Assert.Equal(records ??= described, described);
        }
    }

    // A compilation whose library holds a probe source and whose program is valid, bound once: its Bind is refused (no result). The
    // library is refused by its freeze compilation (the seam, CompilationTestHelper.UseEditedLibrary) and read here, as Of's would be.
    private static Compilation RefusedLibrary(string library)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        CompilationTestHelper.UseEditedLibrary(c, x => x.Kotonoha.CreateCodeContext().Parse(x.Kotonoha.RootKoto, library));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(v: ref/i32) => ()\n");
        Assert.Equal(default, c.Bind());
        return c;
    }
}
