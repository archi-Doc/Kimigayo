// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi.Compiler;
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
    // frozen; a second Bind runs the library pass again and refuses with equal records.
    [Fact]
    public void ARefusedLibraryLeavesOnlyItsRefusal()
    {
        var c = RefusedLibrary(ZUnsupportedProbe + "public group ZUses\n    public func zuse(v: ref/::ZProbe.Inner) => ()\n");
        var first = c.Library.Layer.Refusal!;
        Assert.Single(first.Records);
        Assert.False(first.Syntax);
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
        Assert.Equal(2, published.Length);
        Assert.All(published, x =>
        {
            Assert.Equal(nameof(Kimi.DiagnosticCode.InternalInvariant_Kd), x.Code);
            Assert.Equal(c.Library.Kotonoha.Name, x.Path); // The library's document.
            Assert.Equal("ZProbe", x.Text);
        });
        int[] uses = [library.IndexOf("ref/::ZProbe.Inner", StringComparison.Ordinal) + "ref/::".Length, library.IndexOf("ref/::ZProbe)", StringComparison.Ordinal) + "ref/::".Length];
        Assert.Equal(uses, published.Select(static x => x.Span.Start).Order().ToArray());
    }

    // s6b and s6p (C1 (x); form (R)): a library whose only defect is a syntax error (here a parameter list without its `)`, which the parser
    // reports as three syntax records) is refused by the clause's syntax term; each parser record is published once under its own code, and
    // the refusal holds no copy of one (a Binding record derived from them is left out as Finalize leaves it out).
    [Fact]
    public void ASyntaxOnlyLibraryDefectIsRefusedAndItsRecordStaysTheParsers()
    {
        var c = RefusedLibrary("public group ZSyntax\n    public func f(x: i32 -> i32 => x\n");
        Assert.True(c.Library.Layer.Refusal!.Syntax);
        Assert.False(c.Check());
        var published = TestDiagnostics.Of(c);
        var parsers = published.Where(static x => x.Code is nameof(Kimi.DiagnosticCode.ExpectedSyntax_Kd) or nameof(Kimi.DiagnosticCode.MissingSyntax_Kd)).ToArray();
        Assert.NotEmpty(parsers);
        Assert.Equal(parsers.Length, parsers.Select(static x => (x.Code, x.Span)).Distinct().Count());
        Assert.Equal(published.Length, parsers.Length + c.Library.Layer.Refusal!.Records.Length);
        Assert.DoesNotContain(published, x => x.Code == nameof(Kimi.DiagnosticCode.InternalInvariant_Kd) && parsers.Any(p => p.Span == x.Span));
    }

    // Ruling 33 (F2): the check of a compilation over a refused library publishes only the refusal and builds no MIR.
    [Fact]
    public void ARefusedLibrarysCheckBuildsNoMir()
    {
        var c = RefusedLibrary(ZUnsupportedProbe + "public group ZUses\n    public func zuse(v: ref/::ZProbe.Inner) => ()\n");
        Assert.False(c.Check());
        Assert.Null(typeof(Compilation).GetField("mir", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c));
        Assert.Equal(nameof(Kimi.DiagnosticCode.InternalInvariant_Kd), Assert.Single(TestDiagnostics.Of(c)).Code);
    }

    // A compilation whose library holds a probe source and whose program is valid, bound once: its Bind is refused (no result).
    private static Compilation RefusedLibrary(string library)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Library.Kotonoha.CreateCodeContext().Parse(c.Library.Kotonoha.RootKoto, library);
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(v: ref/i32) => ()\n");
        Assert.Equal(default, c.Bind());
        return c;
    }
}
