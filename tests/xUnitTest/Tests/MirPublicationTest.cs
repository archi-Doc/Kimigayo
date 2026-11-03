// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>Core's publisher of MIR records (R6-check H3-2, design 3.1-3.4; DECISIONS R5-M184): one <c>MirReport.Merged</c> problem per
/// <c>Compilation.Publish</c> call, its key form, condition, causes and broken contracts, observed in the published diagnostics.</summary>
public sealed class MirPublicationTest
{
    // A valid program whose for expansion synthesizes sibling patterns with the loop's span (MirRecordsAtOneLocationAreOneProblem).
    private const string Valid =
        "func sum(values: ref/Array<i32>) -> i32\n    var total = 0\n    for value in values\n        total += value\n    return total\n" +
        "public func main() => ()\n";

    private static readonly DiagnosticRequirement Moved = DiagnosticRequirement.Ownership(OwnershipRequirement.PossiblyMovedUse);
    private static readonly DiagnosticRequirement Limit = DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported);
    private static readonly DiagnosticRequirement Depth = DiagnosticRequirement.Ownership(OwnershipRequirement.DepthLimit);

    // Design 3.2: a located problem (a record class, or a code of category Unsupported, Resource or Internal without Arguments) is keyed by
    // its location, so one at each sibling of a for expansion is one record at the loop.
    [Fact]
    public void ALocatedProblemIsKeyedByItsLocation()
    {
        var (c, program) = Program(Valid);
        var loop = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ForKoto>().Single();
        foreach (var arm in loop.Iteration!.Match.Arms)
        {
            var site = SourceRef.Of(arm.Pattern);
            program.Report(site);
            program.Report(site, DiagnosticCode.OversizedLayout_Kd, MirRecordClass.Collection);
            program.Problems.Add(new(DiagnosticCode.Unsupported_Kd, Limit, site, MirProblem.NoBody, 0b01));
            program.Problems.Add(Found(DiagnosticCode.OwnershipDepthLimit_Kd, Depth, site));
            program.Problems.Add(new(DiagnosticCode.InternalInvariant_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.Internal), site, 0, 0b01));
        }

        Assert.Equal(10, Publish(c, program));
        var errors = Errors(c);
        Assert.All(errors, x => Assert.Equal(loop.Span, x.Span));
        Assert.Equal(
            ["InternalInvariant_Kd", "OversizedLayout_Kd", "OwnershipDepthLimit_Kd", "Unsupported_Kd", "Unsupported_Kd"],
            errors.Select(static x => x.Code).Order(StringComparer.Ordinal));
    }

    // Design 3.2 (Q-R4-4): any other problem is keyed by its subject with its Ordinal as the condition, so two relations at one subject are
    // two records (conditions 0 and 1); a located code's two Reasons at one subject are one record.
    [Fact]
    public void TwoRelationsAtOneSubjectAreTwoConditions()
    {
        var (c, program) = Program(Valid);
        var site = Site(c, Valid, "return total");
        var relation = DiagnosticRequirement.Ownership(OwnershipRequirement.UnprovenOrigin);
        var equality = new MirReason(null, null, true);
        program.Problems.Add(Found(DiagnosticCode.UnsatisfiedOriginRelation_Kd, relation, site));
        program.Problems.Add(new(DiagnosticCode.UnsatisfiedOriginRelation_Kd, relation, site, 0, 0b01, equality));
        program.Problems.Add(Found(DiagnosticCode.Unsupported_Kd, Limit, site));
        program.Problems.Add(new(DiagnosticCode.Unsupported_Kd, Limit, site, 0, 0b01, equality));
        Assert.Equal([0, 1, 0, 1], program.Problems.Merged.ToArray().Select(static x => x.Ordinal));
        Publish(c, program);
        Assert.Equal(
            [("UnsatisfiedOriginRelation_Kd", "return total"), ("UnsatisfiedOriginRelation_Kd", "return total"), ("Unsupported_Kd", "return total")],
            Errors(c).Select(static x => (x.Code, x.Text)).Order());
    }

    // Design 3.4: a MIR cause names its problem by the key KeyOf gives, the subject's or the location's, so a derived problem is suppressed
    // when its cause publishes; published alone, it is PrerequisiteUnavailable_Kd.
    [Fact]
    public void ADerivedProblemNamesItsMirCausesKey()
    {
        foreach (var causes in new[] { true, false })
        {
            var (c, program) = Program(Valid);
            var (subject, located) = (Site(c, Valid, "return total"), Site(c, Valid, "var total = 0"));
            var (step, zero) = (Site(c, Valid, "total += value"), Site(c, Valid, "0"));
            program.Problems.Add(Found(DiagnosticCode.MovedPlace_Kd, Moved, subject));
            program.Problems.Add(Found(DiagnosticCode.Unsupported_Kd, Limit, located));
            program.Problems.Add(Found(DiagnosticCode.MovedPlace_Kd, Moved, step), [new(MirFactKind.Cause, subject, 0)]);
            program.Problems.Add(Found(DiagnosticCode.MovedPlace_Kd, Moved, zero), [new(MirFactKind.Cause, located, 1)]);
            for (var i = causes ? 0 : 2; i < program.Problems.Merged.Length; i++)
            {
                c.Publish(program, i);
            }

            (string, string?)[] expected = causes ? [("MovedPlace_Kd", "return total"), ("Unsupported_Kd", "var total = 0")]
                : [("PrerequisiteUnavailable_Kd", "0"), ("PrerequisiteUnavailable_Kd", "total += value")];
            Assert.Equal(expected, Errors(c).Select(static x => (x.Code, x.Text)).Order());
        }
    }

    // Design 3.4, 5 (synthetic, before 2g-1): a Binding cause (-1) names Binding's causes at its site, so its problem is suppressed by
    // Binding's Error; at a node Binding resolved it has none, a broken contract: InternalInvariant_Kd at the subject.
    [Fact]
    public void ABindingCauseIsBindingsFailureCauses()
    {
        const string Source = "func f() -> i32\n    let a = 1\n    return missing\n";
        var c = CompilationTestHelper.Parse(Source, "Hello.kimi");
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        var program = new MirProgram();
        var (failed, resolved) = (Site(c, Source, "missing"), Site(c, Source, "1"));
        program.Problems.Add(Found(DiagnosticCode.MovedPlace_Kd, Moved, Site(c, Source, "let a = 1")), [new(MirFactKind.Cause, failed, -1)]);
        program.Problems.Add(Found(DiagnosticCode.MovedPlace_Kd, Moved, Site(c, Source, "return missing")), [new(MirFactKind.Cause, resolved, -1)]);
        Publish(c, program);
        Assert.Equal(
            [("InternalInvariant_Kd", "return missing"), ("UnresolvedBinding_Kd", "missing")],
            Errors(c).Select(static x => (x.Code, x.Text)).Order());
    }

    // Design 3.2 (N3), 3.3 (N1): a located problem carries no fact, and the fact table has a row for every kind it meets (Inlined's problem
    // never reaches Merged, since MirReport.Add drops it); a located problem with a fact, a Cause too, and a fact of a kind without a row are
    // broken contracts: InternalInvariant_Kd at the subject, and the problem itself is not published.
    [Fact]
    public void AFactThePublisherCannotPublishIsABrokenContract()
    {
        var (c, program) = Program(Valid);
        var anchor = Site(c, Valid, "var total = 0");
        program.Problems.Add(Found(DiagnosticCode.Unsupported_Kd, Limit, Site(c, Valid, "return total")), [new(MirFactKind.Anchor, anchor)]);
        program.Problems.Add(Found(DiagnosticCode.OwnershipDepthLimit_Kd, Depth, Site(c, Valid, "total += value")), [new(MirFactKind.Cause, anchor, 0)]);
        var zero = Site(c, Valid, "0");
        program.Problems.Add(Found(DiagnosticCode.MovedPlace_Kd, Moved, zero), [new((MirFactKind)byte.MaxValue, anchor)]);
        Publish(c, program);
        Assert.Equal(
            [("InternalInvariant_Kd", "0"), ("InternalInvariant_Kd", "return total"), ("InternalInvariant_Kd", "total += value")],
            Errors(c).Select(static x => (x.Code, x.Text)).Order());
    }

    // A bound compilation of a valid source and an empty program whose report a test fills.
    private static (Compilation Compilation, MirProgram Program) Program(string source)
    {
        var c = CompilationTestHelper.Parse(source, "Hello.kimi");
        Assert.True(c.Bind().IsComplete);
        return (c, new MirProgram());
    }

    // Publishes every merged problem of the program and returns their number.
    private static int Publish(Compilation c, MirProgram program)
    {
        for (var i = 0; i < program.Problems.Merged.Length; i++)
        {
            c.Publish(program, i);
        }

        return program.Problems.Merged.Length;
    }

    private static TestDiagnostic[] Errors(Compilation c) => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error)];

    // The outermost node whose source is the text.
    private static Koto Node(Compilation c, string source, string text)
        => KotoTree.Walk(c.Kotonoha.RootKoto).First(x => x.Span.Length == text.Length && source.AsSpan(x.Span.Start, x.Span.Length).SequenceEqual(text));

    private static SourceRef Site(Compilation c, string source, string text) => SourceRef.Of(Node(c, source, text));

    // An analysis problem of body 0's first view.
    private static MirProblem Found(DiagnosticCode code, DiagnosticRequirement requirement, SourceRef subject) => new(code, requirement, subject, 0, 0b01);
}
