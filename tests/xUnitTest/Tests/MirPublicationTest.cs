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

    // G353's form (PropertyBindingTest's R3): a stored Closure naming its container's Origin, Binding's located limit at the literal.
    private const string ClosureView = "struct View {source}\n    public let data: ref/i32 during source\n" +
        "    public let cb = func (y: ref/i32 during source) => ()\n    public init(data: ref/i32 during source) => self.data = data\n\n" +
        "func f(r: ref/i32) -> i32\n    let view = View.init(r)\n";

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
        program.Problems.Add(new(DiagnosticCode.UnsatisfiedOriginRelation_Kd, relation, site, MirProblem.NoBody, 0b01, equality));
        program.Problems.Add(Found(DiagnosticCode.Unsupported_Kd, Limit, site));
        program.Problems.Add(new(DiagnosticCode.Unsupported_Kd, Limit, site, MirProblem.NoBody, 0b01, equality));
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
    // broken contracts: InternalInvariant_Kd at the subject under Ownership.Internal, as MirReport's defects, so it is one record with an
    // analysis InternalInvariant_Kd there, and the problem itself is not published.
    [Fact]
    public void AFactThePublisherCannotPublishIsABrokenContract()
    {
        var (c, program) = Program(Valid);
        var anchor = Site(c, Valid, "var total = 0");
        var defect = DiagnosticRequirement.Ownership(OwnershipRequirement.Internal);
        program.Problems.Add(Found(DiagnosticCode.InternalInvariant_Kd, defect, Site(c, Valid, "return total")));
        program.Problems.Add(Found(DiagnosticCode.Unsupported_Kd, Limit, Site(c, Valid, "return total")), [new(MirFactKind.Anchor, anchor)]);
        program.Problems.Add(Found(DiagnosticCode.OwnershipDepthLimit_Kd, Depth, Site(c, Valid, "total += value")), [new(MirFactKind.Cause, anchor, 0)]);
        var zero = Site(c, Valid, "0");
        program.Problems.Add(Found(DiagnosticCode.MovedPlace_Kd, Moved, zero), [new((MirFactKind)byte.MaxValue, anchor)]);
        Publish(c, program);
        Assert.Equal(
            [("InternalInvariant_Kd", "0"), ("InternalInvariant_Kd", "return total"), ("InternalInvariant_Kd", "total += value")],
            Errors(c).Select(static x => (x.Code, x.Text)).Order());
    }

    // Design 3.3 (DIAGNOSTICS' A6 fact tables): a fact naming a site is a related location with its role and wording.
    [Theory]
    [InlineData((byte)MirFactKind.Anchor, "borrow", "borrow that created the loan")]
    [InlineData((byte)MirFactKind.Use, "loan", "later use of the value retaining the conflicting loan")]
    [InlineData((byte)MirFactKind.Store, "loan", "value retaining the conflicting loan")]
    [InlineData((byte)MirFactKind.Reservation, "reservation", "conflicting exclusive call reservation")]
    [InlineData((byte)MirFactKind.Evaluated, "default", "the default this call omits, evaluated here, does this")]
    [InlineData((byte)MirFactKind.EffectRead, "declaration", "the call or destruction may read this static")]
    [InlineData((byte)MirFactKind.EffectWrite, "declaration", "the call or destruction may write this static")]
    [InlineData((byte)MirFactKind.EffectUnknown, "declaration", "the call's or destruction's effects are unknown, so they may access this static")]
    public void ASiteFactIsARelatedLocation(byte kind, string role, string label)
    {
        var (c, program) = Program(Valid);
        var (subject, site) = (Site(c, Valid, "return total"), Site(c, Valid, "var total = 0"));
        program.Problems.Add(Found(DiagnosticCode.MovedPlace_Kd, Moved, subject), [new((MirFactKind)kind, site)]);
        Publish(c, program);
        var record = Assert.Single(Records(c));
        Assert.Equal(nameof(DiagnosticCode.MovedPlace_Kd), record.Code);
        var related = Assert.Single(record.Related!);
        Assert.Equal((role, "var total = 0", label), (related.Role, Text(Valid, related.Span), related.Label));
    }

    // Design 3.3: a storage limit's Required, Limit and Table facts are OwnershipStorageLimit_Kd's requiredBytes, limitBytes and table.
    [Fact]
    public void AStorageLimitsFactsAreItsArgumentsAndTable()
    {
        var (c, program) = Program(Valid);
        var body = Site(c, Valid, "return total");
        var problem = Found(DiagnosticCode.OwnershipStorageLimit_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.StorageLimit), body);
        var table = (long)MirStorageTable.Loans;
        program.Problems.Add(problem, [new(MirFactKind.Required, body, 4096), new(MirFactKind.Limit, body, 1024), new(MirFactKind.Table, body, table)]);
        Publish(c, program);
        var record = Assert.Single(Records(c));
        Assert.Equal(nameof(DiagnosticCode.OwnershipStorageLimit_Kd), record.Code);
        Assert.Equal([("requiredBytes", "4096"), ("limitBytes", "1024"), ("table", "Loans")], record.Reason!.Select(static x => (x.Name, x.Value)));
    }

    // Design 3.3 (Q-BU-1): an Origin problem's Relation fact is Binding.OriginRelationFacts' evidence, from its C1-F4 row (a fit, with its
    // destination); a containment edge (-1) has no row: wellFormed, without a destination.
    [Fact]
    public void ARelationFactIsTheOriginRelationsEvidence()
    {
        const string Source = "func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    return local@ref\npublic func main() => ()\n";
        var c = MirBuildTest.Compile(Source);
        var program = c.BuildMir();
        var refuted = Index(program, DiagnosticCode.UnsatisfiedOriginRelation_Kd);
        var problem = program.Problems.All[program.Problems.Members(refuted)[0]];
        var edge = Site(c, Source, "let local: i32 = 1");
        program.Problems.Add(problem with { Subject = edge }, [new(MirFactKind.Relation, edge, -1)]);
        c.Publish(program, refuted);
        c.Publish(program, program.Problems.Merged.Length - 1);
        Assert.Equal(
            [
                ("let local: i32 = 1", "relation longer shorter source", "wellFormed"),
                ("return local@ref", "relation longer shorter source destination", "fit"),
            ],
            Records(c).Select(x => (Text(Source, x.Span), string.Join(' ', x.Reason!.Select(static r => r.Name)), x.Reason!.Single(IsSource).Value)).Order());

        static bool IsSource(DiagnosticValue value) => value.Name == "source";
    }

    // Design 3.3 (BU-82, Q-BU-2): the case text is Binding.CaseFact's over each member's views masked by MirCases.Count, so a producer's
    // every view (ulong.MaxValue) is every case, which names none; it is given only when every member gives the same text, and a NoBody
    // member gives none.
    [Fact]
    public void TheCaseTextIsTheMembersMaskedViews()
    {
        const string Source = "func f<s/T>(value: s/T, x: T) -> ()\n    s is owner or uniq\n    T is Copy\n    var v = value@move\n    let local = v\n" +
            "    v@follow = x\n    _ = local@follow\npublic func main() -> ()\n    var n: i32 = 1\n    f(n, 9)\n";
        var c = MirBuildTest.Compile(Source);
        var program = c.BuildMir();
        var bodies = program.Bodies.ToArray().Where(static x => x.Source.Module == 0).ToArray();
        var (f, main) = (bodies.Single(static x => x.Binders.Count > 0).Ordinal, bodies.First(static x => x.Binders.Count == 0).Ordinal);
        var start = program.Problems.Merged.Length;
        Add("var v = value@move", (f, ulong.MaxValue));
        Add("let local = v", (f, 0b10));
        Add("_ = local@follow", (f, 0b10), (main, 0b01));
        Add("f(n, 9)", (f, 0b10), (MirProblem.NoBody, 0b01));
        for (var i = start; i < program.Problems.Merged.Length; i++)
        {
            c.Publish(program, i);
        }

        Assert.Equal(
            [
                ("_ = local@follow", null, null), ("f(n, 9)", null, null), ("let local = v", "s = uniq", "the Semantics binding s"),
                ("var v = value@move", null, null),
            ],
            Records(c).Select(x => (Text(Source, x.Span), x.Reason?.Single().Value, x.Related?.SingleOrDefault(IsDeclaration)?.Label)).Order());

        void Add(string subject, params (int Body, ulong Views)[] members)
        {
            foreach (var (body, views) in members)
            {
                program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, Site(c, Source, subject), body, views));
            }
        }

        static bool IsDeclaration(DiagnosticRelated related) => related.Role == "declaration";
    }

    // Design 5 (BU-2's four shapes, synthetic before 2g-1): where Binding leaves the node it reports at resolved (a rejected assignment
    // target, a resolved recovery node, an unreadable char literal), the node has no FailureCauses, so a Binding cause there is a broken
    // contract (2g-1 sites it at the failed node): InternalInvariant_Kd at the subject. G353's Field selection now rests on the Property's
    // refusal (DECISIONS BU-73), so its cause names Binding's located limit and the problem is suppressed by it.
    [Theory]
    [InlineData("public func main()\n    let x = 1\n    x = 2\n", "x = 2", "x", false)]
    [InlineData(ClosureView + "    let c = view.cb\n    return 1\npublic func main() => ()\n", "return 1", "view.cb", true)]
    [InlineData("public func main()\n    let t = (1, 2\n", "let t = (1, 2", "(1, 2", false)]
    [InlineData("public func main()\n    let c = 'ab'\n", "let c = 'ab'", "'ab'", false)]
    public void ABindingCauseNamesBindingsCausesAtItsSite(string source, string subject, string site, bool sited)
    {
        var c = CompilationTestHelper.Parse(source, "Hello.kimi");
        c.Bind();
        c.Binding.ReportDiagnostics();
        var before = Errors(c).Length;
        var program = new MirProgram();
        var at = Node(c, source, site);
        Assert.Equal(sited, c.Binding.FailureCauses(at) is not null);
        program.Problems.Add(Found(DiagnosticCode.MovedPlace_Kd, Moved, Site(c, source, subject)), [new(MirFactKind.Cause, SourceRef.Of(at), -1)]);
        Publish(c, program);
        var added = Errors(c).Where(static x => x.Code is nameof(DiagnosticCode.InternalInvariant_Kd) or nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
        (string, string?)[] expected = sited ? [] : [(nameof(DiagnosticCode.InternalInvariant_Kd), subject)];
        Assert.Equal(expected, added.Select(static x => (x.Code, x.Text)));
        Assert.Equal(before + (sited ? 0 : 1), Errors(c).Length);
    }

    // H0a 5.2 (A; PLAN G128): where one call meets G128's bound, the collector's Unsupported_Kd and A5's OwnershipDepthLimit_Kd both publish,
    // two located records of one implementation bound at Chain<i32>'s need(c@move) (ComponentSummaryTest's program; built without Analyze,
    // so the difference ledger, which observes Analyze, does not count it twice).
    [Fact]
    public void ChainsBoundIsTwoRecordsAtOneCall()
    {
        var c = MirBuildTest.Compile(
            "struct Chain<T>\n    public let next: Option<obj/Chain<(T, T)>>\n    public init()\n        self.next = .None\n" +
            "func need<T>(x: T)\n    T is Owned\n    ()\n" + "let c = Chain<i32>.init()\nneed(c@move)\nConsole.writeLine(\"chain\")");
        Publish(c, c.BuildMir());
        Assert.Equal(["OwnershipDepthLimit_Kd", "Unsupported_Kd"], Errors(c).Where(static x => x.Text == "need(c@move)").Select(static x => x.Code).Order());
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

    // The published records with their Reason and related locations.
    private static CheckDiagnostic[] Records(Compilation c) => c.Diagnostics.Finalize(DiagnosticPartition.Input, DiagnosticPartition.Emission).Diagnostics;

    private static string Text(string source, SourceSpan? span) => source.Substring(span!.Value.Start, span.Value.Length);

    // The index in Merged of the program's one merged problem of a code.
    private static int Index(MirProgram program, DiagnosticCode code)
    {
        var merged = program.Problems.Merged.ToArray();
        return Array.IndexOf(merged, Assert.Single(merged, x => x.Code == code));
    }

    private static TestDiagnostic[] Errors(Compilation c) => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error)];

    // The outermost node whose source is the text.
    private static Koto Node(Compilation c, string source, string text)
        => KotoTree.Walk(c.Kotonoha.RootKoto).First(x => x.Span.Length == text.Length && source.AsSpan(x.Span.Start, x.Span.Length).SequenceEqual(text));

    private static SourceRef Site(Compilation c, string source, string text) => SourceRef.Of(Node(c, source, text));

    // An analysis problem no body describes, so it has no case text.
    private static MirProblem Found(DiagnosticCode code, DiagnosticRequirement requirement, SourceRef subject)
        => new(code, requirement, subject, MirProblem.NoBody, 0b01);
}
