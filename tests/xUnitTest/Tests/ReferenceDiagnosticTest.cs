// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 9.6.1, 10.5, 23.3.6.1: a container outside constructor inference needs its Type arguments, with counts on invalid formation; a
// reference form that is not yet implemented is one located Unsupported record, also when a later use reads its binding.
public class ReferenceDiagnosticTest
{
    private const string Box = "struct Box<T>\n    public var value: T\n\n    public init(value: T) => self.value = value@move\n\n    public func size(self: ref/Self) -> isize => 1\n";

    private const string Holder = "struct Holder {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n    public func peek(self: ref/Self) -> i32 => self.item@follow\n";

    private const string Helpers = Holder + "\n    public func zero() -> i32 => 0\n\n    public func twice(value: ref/i32 during a) -> i32 => value@follow * 2\n";

    private const string View = "struct View<T> {source}\n    public let item: ref/T during source\n\n    public init(item: ref/T during source) => self.item = item\n\n    public func zero() -> i32 => 0\n\n    public func twice(value: ref/i32 during source) -> i32 => value@follow * 2\n";

    private const string Pin = "struct Pin {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n    public func first<T>(value: ref/T during a) -> i32 => 5\n";

    private const string Outer = "struct Outer<T>\n    public struct Inner {a}\n        public let item: ref/i32 during a\n\n        public init(item: ref/i32 during a) => self.item = item\n\n        public func twice(value: ref/i32 during a) -> i32 => value@follow * 2\n";

    private const string Viewed = "let s = [1, 2, 3]\nlet view = s[0..]\n";

    // A container whose member names no slot in its parameters.
    private const string Tag = "struct Tag {a}\n    public func add(k: i32) -> i32 => k + 1\n";

    // A container with one slot, whose methods see the slot x.
    private const string Slotted = "struct S {x}\n    public let item: ref/i32 during x\n\n    public init(item: ref/i32 during x) => self.item = item\n\n";

    private const string RunTail = "let n = 3\nlet h = Holder.init(n@ref)\nrequire run(h@ref) == 3 else => $abort(\"run\")";

    [Theory]
    [InlineData(Box + "let s = Box.size", "Box", "only the construction target itself can infer its own slots")]
    [InlineData(Box + "let b: Box = Box<i32>.init(4)", "Box", "declares 1 Type parameter, and 0 Type arguments are written")]
    [InlineData(Box + "let b: Box<i32, bool> = Box<i32>.init(4)", "Box<i32, bool>", "declares 1 Type parameter, and 2 Type arguments are written")]
    public void AContainerWithoutItsTypeArgumentsCannotFormAType(string source, string text, string note)
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.InvalidTypeFormation_Kd), error.Code);
        Assert.Equal(text, error.Text);
        Assert.Contains(note, error.Note, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal("declaration", Assert.Single(record.Related!).Role);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(note, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    [Theory]
    [InlineData("Box.init(4)")]
    [InlineData("Box<i32>.init(4)")]
    public void AConstructionTargetCanOmitItsOwnArguments(string expression)
        => Assert.Empty(DiagnosticCorpus.Check(Box + "let b = " + expression).Diagnostics);

    [Theory]
    [InlineData(Holder + "let n = 3\nlet h = Holder.init(n@ref)\nlet p = Holder.peek\nrequire p(h@ref) == 3 else => $abort(\"p\")", "Holder.peek")]
    public void AnUnimplementedReferenceIsOneLocatedUnsupportedRecord(string source, string text)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.Unsupported_Kd), error.Code);
        Assert.Equal(text, error.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARequirementReferenceCanBeStoredAndCalled(bool called)
    {
        var source = "func order<T>(a: ref/T, b: ref/T) -> i32\n    T is Comparable\n    let c = T.compare\n    return " + (called ? "c(a, b)" : "0") + "\n()";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Fact]
    public void ErasingARequirementStillNeedsOwnedSignatureTypes()
    {
        const string Source = "func order<T>(a: ref/T, b: ref/T) -> i32\n    T is Comparable\n    let c: (ref/T, ref/T) -> i32 = T.compare\n    return c(a, b)\n()";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnprovenConstraint_Kd), error.Code);
        Assert.Equal("T.compare", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Empty(DiagnosticCorpus.Check(Source.Replace("T is Comparable", "T is Comparable and Owned", StringComparison.Ordinal)).Diagnostics);
    }

    // SPEC 15.4.4, 9.6.1.1, 23.3.6.1: an Origin slot that a called member's expression qualifier omits, or names by a binding set that no
    // clause relates, is inferred from the call. Binding infers it only in a local initializer, for a called function that is neither
    // generic nor a member of a generic Type, from an argument lent at a parameter's own borrow Origin, so any other such call is one
    // location-only Unsupported_Kd at the qualifier. These were NoApplicableOverload_Kd, MissingOriginBinding_Kd or UnprovenConstraint_Kd
    // (N27a). A Slice qualifier, whose slot is the Type's own Origin, was NoApplicableOverload_Kd outside an initializer and passed the
    // check inside one, as did a generic Type or member there, and then failed generation (GenerationFailed_Kd). A parenthesized callee is
    // a call qualifier too (it was MissingOriginBinding_Kd).
    [Theory]
    [InlineData(Holder + "func run(h: ref/Holder{x}) -> i32\n    let direct = Holder.peek(h)\n    return direct\nlet n = 3\nlet h = Holder.init(n@ref)\nrequire run(h@ref) == 3 else => $abort(\"run\")", "Holder")]
    [InlineData(Holder + "let n = 3\nlet h = Holder.init(n@ref)\nlet e = Holder.peek(h@ref)\nrequire e == 3 else => $abort(\"e\")", "Holder")]
    [InlineData(Holder + "let n = 3\nlet h = Holder.init(n@ref)\nrequire Holder.peek(h@ref) == 3 else => $abort(\"e\")", "Holder")]
    [InlineData(Holder + "func run(h: ref/Holder{x}) -> i32 => Holder.peek(h)\nlet n = 3\nlet h = Holder.init(n@ref)\nrequire run(h@ref) == 3 else => $abort(\"run\")", "Holder")]
    [InlineData(Holder + "let n = 3\nlet h = Holder.init(n@ref)\nlet e = (Holder{w}).peek(h@ref)\nrequire e == 3 else => $abort(\"e\")", "(Holder{w})")]
    [InlineData(Helpers + "let z = Holder.zero()\nrequire z == 0 else => $abort(\"z\")", "Holder")]
    [InlineData(Helpers + "let n = 3\nrequire Holder.twice(n@ref) == 6 else => $abort(\"t\")", "Holder")]
    [InlineData(Helpers + "let n = 3\nlet v = 1\nlet q = 2\nrequire Holder.twice(n@ref) == 6 else => $abort(\"t\")", "Holder")]
    [InlineData(Helpers + "func run(n: ref/i32) -> i32\n    let f = func [n] () => Holder.twice(n)\n    return 6\nlet n = 3\nrequire run(n@ref) == 6 else => $abort(\"t\")", "Holder")]
    [InlineData(Viewed + "require Slice<i32>.contains(view, 3@ref) else => $abort(\"d\")", "Slice<i32>")]
    [InlineData(Viewed + "let c = Slice<i32>.contains(view, 2@ref)\nrequire c else => $abort(\"c\")", "Slice<i32>")]
    [InlineData("func count(view: Slice<i32>) -> i32\n    var total = 0\n    for item in Slice<i32>.iterate(view@ref)\n        total += item@follow\n    return total\nlet s = [1, 2, 3]\nrequire count(s[0..]) == 6 else => $abort(\"d\")", "Slice<i32>")]
    [InlineData(View + "require View<i32>.zero() == 0 else => $abort(\"z\")", "View<i32>")]
    [InlineData(View + "let n = 3\nlet t = View<i32>.twice(n@ref)\nrequire t == 6 else => $abort(\"t\")", "View<i32>")]
    [InlineData(View + "let n = 3\nlet u = (View<i32>{p}).twice(n@ref)\nrequire u == 6 else => $abort(\"t\")", "(View<i32>{p})")]
    [InlineData(Pin + "let n = 3\nlet t = Pin.first(n@ref)\nrequire t == 5 else => $abort(\"t\")", "Pin")]
    [InlineData(Outer + "let n = 3\nlet t = Outer<i32>.Inner.twice(n@ref)\nrequire t == 6 else => $abort(\"t\")", "Outer<i32>.Inner")]
    [InlineData(Helpers + "let n = 3\nlet h = Holder.init(n@ref)\nrequire (Holder{w}).peek(h@ref) == 3 else => $abort(\"e\")", "(Holder{w})")]
    [InlineData(Helpers + "let n = 3\nrequire (Holder{w2}).twice(n@ref) == 6 else => $abort(\"t\")", "(Holder{w2})")]
    [InlineData(Helpers + "let n = 3\nrequire (Holder.twice)(n@ref) == 6 else => $abort(\"t\")", "Holder")]
    [InlineData(Helpers + "func generic<T>(x: T, n: ref/i32) -> i32\n    return Holder.twice(n)\nlet n = 3\nrequire generic(true, n@ref) == 6 else => $abort(\"g\")", "Holder")]
    [InlineData(Helpers + "let n = 3\nlet t = Holder.twice(n@ref)\nrequire t == 6 else => $abort(\"t\")", "Holder")]
    [InlineData(Helpers + "let n = 3\nlet t = (Holder.twice)(n@ref)\nrequire t == 6 else => $abort(\"t\")", "Holder")]
    public void AnOpenQualifierSlotIsOneLocatedLimitAtTheQualifier(string source, string text)
    {
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.Unsupported_Kd), text), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        Assert.Equal((DiagnosticCategory.Unsupported, null, null), (error.Category, error.Note, error.Related));
    }

    // The limit leaves independent problems of the call visible: an unresolved argument keeps its own record, a missing member is
    // UnresolvedBinding_Kd without a limit, and an independent error elsewhere stays.
    [Fact]
    public void AnOpenQualifierSlotKeepsIndependentProblems()
    {
        const string Source = Holder + "let n = 3\nlet h = Holder.init(n@ref)\nlet e = Holder.peek(missing)\nlet g = Holder.peeek(h@ref)\nlet k: bool = 5";
        var output = DiagnosticCorpus.Check(Source);
        Assert.Equal(
            [(nameof(DiagnosticCode.Unsupported_Kd), "Holder"), (nameof(DiagnosticCode.UnresolvedBinding_Kd), "missing"), (nameof(DiagnosticCode.UnresolvedBinding_Kd), "Holder.peeek"), (nameof(DiagnosticCode.TypeMismatch_Kd), "5")],
            output.Diagnostics.Select(x => (x.Code, x.Span is { } span ? Source.Substring(span.Start, span.Length) : string.Empty)).ToArray());
    }

    // The limit is the same public record in the CLI rendering, the JSON output and both language-server placements.
    [Fact]
    public void AnOpenQualifierSlotIsPublishedAtTheQualifier()
    {
        const string Source = Holder + "let n = 3\nlet h = Holder.init(n@ref)\nlet e = Holder.peek(h@ref)\nrequire e == 3 else => $abort(\"e\")";
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal((nameof(DiagnosticCode.Unsupported_Kd), "Holder"), (error.Code, error.Text));
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Unsupported, record.Category);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("This form is outside the implemented subset", console.Text, StringComparison.Ordinal);
        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("\"code\":\"Unsupported_Kd\"", json, StringComparison.Ordinal);
        Assert.Contains("\"category\":\"Unsupported\"", json, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    // SPEC 15.3.1, 23.3.6.4: `{x}` on a qualifier introduces a new set name and never applies the visible set x, so each reuse is one
    // DuplicateBinding_Kd at the set with the first declaration related. The call and the reference that rest on the failed qualifier
    // report nothing more; they were NoApplicableOverload_Kd and Unsupported_Kd.
    [Fact]
    public void AReusedSetOnAQualifierIsOnlyTheDuplicate()
    {
        const string Source = Holder + "func run(h: ref/Holder{x}) -> i32\n    let direct = (Holder{x}).peek(h)\n    let p = (Holder{x}).peek\n    return direct + p(h)\nlet n = 3\nlet h = Holder.init(n@ref)\nrequire run(h@ref) == 6 else => $abort(\"run\")";
        var output = DiagnosticCorpus.Check(Source);
        Assert.Equal(2, output.Diagnostics.Length);
        var first = Source.IndexOf("Holder{x}", StringComparison.Ordinal);
        foreach (var error in output.Diagnostics)
        {
            Assert.Equal((nameof(DiagnosticCode.DuplicateBinding_Kd), "Holder{x}"), (error.Code, error.Span is { } span ? Source.Substring(span.Start, span.Length) : string.Empty));
            Assert.Equal("'x' is declared again", error.Label);
            Assert.Equal("A binding set introduces a new set name and never applies an existing Origin or set (SPEC 15.3.1); x is already declared in this scope", error.Note);
            var related = Assert.Single(error.Related!);
            Assert.Equal(("declaration", first), (related.Role, related.Span!.Value.Start));
        }
    }

    // SPEC 15.3.3, 15.4.4: outside a local initializer, such as in a return statement, no declaration holds the qualifier, so the reuse
    // relates only the first declaration; a new set written in a local declaration and related there leaves no diagnostic.
    [Fact]
    public void AReusedSetOutsideADeclarationIsRelatedOnANewLocal()
    {
        const string Head = Holder + "func run(h: ref/Holder{x}) -> i32\n";
        const string Tail = "let n = 3\nlet h = Holder.init(n@ref)\nrequire run(h@ref) == 3 else => $abort(\"run\")";
        const string Source = Head + "    return (Holder{x}).peek(h)\n" + Tail;
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.DuplicateBinding_Kd), Source.LastIndexOf("Holder{x}", StringComparison.Ordinal)), (error.Code, error.Span!.Value.Start));
        Assert.Equal(("declaration", "first declaration", Source.IndexOf("Holder{x}", StringComparison.Ordinal)), (Assert.Single(error.Related!).Role, error.Related![0].Label, error.Related![0].Span!.Value.Start));
        Assert.Empty(DiagnosticCorpus.Check(Head + "    let v = (Holder{x2}).peek(h)\n        origin x2.a == x.a\n    return v\n" + Tail).Diagnostics);
    }

    // SPEC 15.3.1, 15.3.2: a set that reuses the name of a container's own slot relates that slot's header declaration, which it did not,
    // and the use of the name beside it resolves to the slot instead of failing again at the set (an InvalidOriginBinding_Kd cascade). The
    // one-slot `during` form and a new related set both leave no diagnostic.
    [Fact]
    public void AReusedSlotNameRelatesTheSlot()
    {
        const string Head = "struct Holder {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n";
        const string Tail = "let n = 3\nrequire n == 3 else => $abort(\"n\")";
        const string Source = Head + "    public func make(value: ref/i32 during a) -> Holder{a} => Holder.init(value)\n" + Tail;
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.DuplicateBinding_Kd), "Holder{a}"), (error.Code, Source.Substring(error.Span!.Value.Start, error.Span!.Value.Length)));
        Assert.Equal("'a' is declared again", error.Label);
        var related = Assert.Single(error.Related!);
        Assert.Equal(("declaration", "Origin slot", new SourceSpan(Source.IndexOf("{a}", StringComparison.Ordinal) + 1, 1)), (related.Role, related.Label, related.Span!.Value));
        Assert.Empty(DiagnosticCorpus.Check(Head + "    public func make(value: ref/i32 during a) -> Holder during a => Holder.init(value)\n" + Tail).Diagnostics);
        Assert.Empty(DiagnosticCorpus.Check(Head + "    public func make(value: ref/i32 during a) -> Holder{a2}\n        origin a2.a == a\n        return Holder.init(value)\n" + Tail).Diagnostics);
    }

    // SPEC 15.3.3, 7.3: a Field initializer, a parameter default, a base initializer and an anonymous function in either keep the limit
    // at the qualifier.
    [Theory]
    [InlineData("struct S\n    public var v: i32 = Holder.zero()\n\n    public init() => self.v = 1\n", "Holder")]
    [InlineData("struct S\n    public var v: i32 = (Holder{w}).zero()\n\n    public init() => self.v = 1\n", "(Holder{w})")]
    [InlineData("struct S\n    public var v: i32 = Holder.twice(5@ref)\n\n    public init() => self.v = 1\n", "Holder")]
    [InlineData("struct S\n    public var v: i32 = (Holder{w}).twice(5@ref)\n\n    public init() => self.v = 1\n", "(Holder{w})")]
    [InlineData("struct S\n    public var v: i32 = Holder.peek(Holder.init(5@ref)@ref)\n\n    public init() => self.v = 1\n", "Holder")]
    [InlineData(Tag + "struct S\n    public var v: i32 = Tag.add(4)\n\n    public init() => self.v = 1\n", "Tag")]
    [InlineData(Tag + "struct S\n    public var v: i32 = (Tag{w}).add(4)\n\n    public init() => self.v = 1\n", "(Tag{w})")]
    [InlineData("struct S\n    public var g: () -> i32 = func () => Holder.zero()\n\n    public init() => ()\n", "Holder")]
    [InlineData("func f(k: i32 = Holder.zero()) -> i32 => k\n", "Holder")]
    [InlineData("func f(n: ref/i32, k: i32 = Holder.twice(n)) -> i32 => k\n", "Holder")]
    [InlineData("func f(k: i32 = (Holder{q}).zero()) -> i32 => k\n", "(Holder{q})")]
    [InlineData("func f(h: ref/Holder, k: i32 = Holder.peek(h)) -> i32 => k\n", "Holder")]
    [InlineData("func f(g: () -> i32 = func () => Holder.zero()) -> i32 => g()\n", "Holder")]
    [InlineData("public open struct Named\n    public let k: i32\n\n    protected init(k: i32)\n        self.k = k\n\npublic struct Entry : Named\n    public let number: i32\n\n    public init(number: i32) : base(Holder.zero())\n        self.number = number\n", "Holder")]
    public void AnOpenQualifierSlotOutsideABodyIsOneLocatedLimit(string declarations, string text)
    {
        var source = Helpers + declarations + "let n = 3\nrequire n == 3 else => $abort(\"n\")";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.Unsupported_Kd), source.IndexOf(text + ".", Helpers.Length, StringComparison.Ordinal), text.Length), (error.Code, error.Span!.Value.Start, error.Span!.Value.Length));
    }

    [Fact]
    public void AStaticQualifierSlotDefaultsWithoutAnUnnecessaryRepair()
    {
        const string Source = Helpers + "group G\n    public let v: i32 = Holder.zero()\nrequire G.v == 0 else => $abort(\"static qualifier\")";
        Assert.Empty(DiagnosticCorpus.Check(Source).Diagnostics);
        ScalarEmissionTest.EmitFixture("ReferenceStaticQualifier", Source, string.Empty);
    }

    [Fact]
    public void AStaticQualifierDoesNotExtendATemporaryBorrow()
    {
        const string Source = Helpers + "group G\n    public let v: i32 = Holder.twice(5@ref)\nlet present = 1";
        var record = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal("UnsatisfiedOriginRelation_Kd", record.Code);
        Assert.Equal(new SourceSpan(Source.IndexOf("5@ref", StringComparison.Ordinal), 5), record.Span);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Empty(record.Repairs ?? []);
    }

    // SPEC 15.3.3: a set related on a Field's declaration runs, on an instance Field and a group's Field, related to static and to the
    // container's slot, and so does a local declaration in an anonymous function's body inside a Field initializer.
    [Fact]
    public void AQualifierSlotRelatedOnAFieldRuns()
    {
        const string Stored = "struct S\n    public var v: i32 = (Holder{q}).zero()\n        origin q.a == static\n    public var u: i32 = (Holder{w}).zero()\n        origin w.a == static\n    public var g: () -> i32 = func () -> i32\n        let t = (Holder{r}).zero()\n            origin r.a == static\n        return t + 4\n\n    public init() => ()\n";
        const string Slot = "struct T {x}\n    public let item: ref/i32 during x\n    public var v: i32 = (Holder{x2}).zero()\n        origin x2.a == x\n\n    public init(item: ref/i32 during x) => self.item = item\n";
        const string Static = "group G\n    public let v: i32 = (Holder{q}).zero()\n        origin q.a == static\n";
        const string Body = "let n = 3\nlet s = S.init()\nlet t = T.init(n@ref)\nlet gv = G.v\nrequire s.v == 0 and s.u == 0 and s.g() == 4 and t.v == 0 and t.item@follow == 3 and gv == 0 else => $abort(\"stored\")";
        ScalarEmissionTest.EmitFixture("OriginQualifierStored", Helpers + Stored + Slot + Static + Body, string.Empty);
    }

    // SPEC 15.3.1, 15.3.3: a reused set in a stored Field's initializer or a parameter default is one DuplicateBinding_Kd that relates
    // the container's slot it reuses, and a new set related on the Field's declaration leaves no diagnostic.
    [Fact]
    public void AReusedSetOutsideABodyRelatesTheReusedSlot()
    {
        const string Head = "struct S {x}\n    public let item: ref/i32 during x\n";
        const string Init = "\n    public init(item: ref/i32 during x) => self.item = item\n";
        const string Tail = "let n = 3\nlet s = S.init(n@ref)\nrequire s.item@follow == 3 else => $abort(\"s\")";
        var slot = new SourceSpan(Helpers.Length + Head.IndexOf("{x}", StringComparison.Ordinal) + 1, 1);
        foreach (var member in new[]
        {
            "    public var v: i32 = (Holder{x}).zero()\n",
            "    public var v: i32 = (Holder{x}).twice(5@ref)\n",
            "\n    public func get(self: ref/Self, k: i32 = (Holder{x}).zero()) -> i32 => k\n",
        })
        {
            var source = Helpers + Head + (member[0] == '\n' ? Init + member : member + Init) + Tail;
            var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
            Assert.Equal((nameof(DiagnosticCode.DuplicateBinding_Kd), new SourceSpan(source.IndexOf("Holder{x}", StringComparison.Ordinal), 9)), (error.Code, error.Span!.Value));
            var related = Assert.Single(error.Related!);
            Assert.Equal(("declaration", "Origin slot", slot), (related.Role, related.Label, related.Span!.Value));
        }

        Assert.Empty(DiagnosticCorpus.Check(Helpers + Head + "    public var v: i32 = (Holder{x2}).zero()\n        origin x2.a == x\n" + Init + Tail).Diagnostics);
    }

    // SPEC 15.3.3: the Field forms that relate a set run for a member whose parameters omit the slot, related to static and to the
    // container's slot.
    [Fact]
    public void AFieldQualifierWhoseParametersOmitTheSlotRuns()
    {
        const string Stored = "struct S\n    public var v: i32 = (Tag{q}).add(4)\n        origin q.a == static\n    public var u: i32 = (Tag{w}).add(5)\n        origin w.a == static\n\n    public init() => ()\n";
        const string Slot = "struct T {x}\n    public let item: ref/i32 during x\n    public var v: i32 = (Tag{x2}).add(6)\n        origin x2.a == x\n\n    public init(item: ref/i32 during x) => self.item = item\n";
        const string Body = "let n = 3\nlet s = S.init()\nlet t = T.init(n@ref)\nrequire s.v == 5 and s.u == 6 and t.v == 7 and t.item@follow == 3 else => $abort(\"stored\")";
        Assert.Empty(DiagnosticCorpus.Check(Tag + Stored + Slot + Body).Diagnostics);
        ScalarEmissionTest.EmitFixture("OriginQualifierStoredArguments", Tag + Stored + Slot + Body, string.Empty);
    }

    // SPEC 15.3.1, 23.3.6.4: a set that failed as a repeat declares nothing, and a name that resolves to it, in a clause attached to
    // the same declaration, in a later declaration or as `during a` beside it, rests on its DuplicateBinding_Kd. Resolving the name to
    // the outer declaration it repeats instead reported relations the program never wrote (UnprovenOriginRelation_Kd "requires
    // h.a == static", InvalidOriginBinding_Kd at a Field's clause).
    [Theory]
    [InlineData(Holder + "func run(h: ref/Holder{x}) -> i32\n    let k: ref/Holder{x} = h\n        origin x.a == static\n    return k.peek()\n" + RunTail, "Holder{x} = h")]
    [InlineData(Holder + "func run(h: ref/Holder{x}, g: ref/Holder{y}) -> i32\n    let k: ref/Holder{y} = h\n        origin y.a == x.a\n    return k.peek()\nlet n = 3\nlet h = Holder.init(n@ref)\nrequire run(h@ref, h@ref) == 3 else => $abort(\"run\")", "Holder{y} = h")]
    [InlineData(Helpers + "func run(h: ref/Holder{x}) -> i32\n    let v = (Holder{x}).zero()\n        origin x.a == static\n    return v\n" + RunTail, "Holder{x}).zero")]
    [InlineData(Helpers + "struct S {x}\n    public let item: ref/i32 during x\n    public var v: i32 = (Holder{x}).zero()\n        origin x.a == static\n\n    public init(item: ref/i32 during x) => self.item = item\nlet n = 3\nlet s = S.init(n@ref)\nrequire s.v == 0 else => $abort(\"s\")", "Holder{x}).zero")]
    [InlineData(Holder + "func run(h: ref/Holder{x}) -> i32\n    let k: ref/Holder{x} = h\n    let j: ref/i32 during x.a = k.item\n    return j@follow\n" + RunTail, "Holder{x} = h")]
    [InlineData(Holder + "struct Box {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n    public func make(value: ref/i32 during a) -> Holder{a}\n        origin a.a == a\n        return Holder.init(value)\nlet n = 3\nrequire n == 3 else => $abort(\"n\")", "Holder{a}\n")]
    public void ARepeatedSetRestsItsUsesOnTheDuplicate(string source, string set)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.DuplicateBinding_Kd), new SourceSpan(source.IndexOf(set, StringComparison.Ordinal), 9)), (error.Code, error.Span!.Value));
    }

    // SPEC 23.3.6.4: a problem of a clause that does not rest on the repeated name stays visible beside the repeat, as in the program
    // without the repeat: an unresolved Origin name, a slot name the set's Type does not declare, and an independent Type mismatch.
    [Theory]
    [InlineData("origin x.a == nosuch.a\n", nameof(DiagnosticCode.InvalidOriginBinding_Kd), "nosuch.a")]
    [InlineData("origin x.b == static\n", nameof(DiagnosticCode.InvalidOriginBinding_Kd), "b == static")]
    [InlineData("origin x.a == x.a\n    let m: bool = 3\n", nameof(DiagnosticCode.TypeMismatch_Kd), "3\n    return")]
    public void ARepeatedSetKeepsIndependentProblems(string clause, string code, string at)
    {
        var source = Holder + "func run(h: ref/Holder{x}) -> i32\n    let k: ref/Holder{x} = h\n        " + clause + "    return k.peek()\n" + RunTail;
        var output = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(2, output.Length);
        Assert.Equal((nameof(DiagnosticCode.DuplicateBinding_Kd), source.IndexOf("Holder{x} = h", StringComparison.Ordinal)), (output[0].Code, output[0].Span!.Value.Start));
        Assert.Equal((code, source.IndexOf(at, StringComparison.Ordinal)), (output[1].Code, output[1].Span!.Value.Start));
        var written = source.Replace("Holder{x} = h\n        origin x.", "Holder{x2} = h\n        origin x2.", StringComparison.Ordinal);
        var alone = Assert.Single(DiagnosticCorpus.Check(written).Diagnostics);
        Assert.Equal((code, written.IndexOf(at, StringComparison.Ordinal)), (alone.Code, alone.Span!.Value.Start));
    }

    // SPEC 15.3.1: a reused set is one DuplicateBinding_Kd at the set in every position that writes one, relating the first declaration.
    [Theory]
    [InlineData(Holder + "func run(h: ref/Holder{x}) -> i32\n    let f = func (g: ref/Holder{x}) -> i32 => g.peek()\n    return f(h)\n" + RunTail, "Holder{x}) -> i32 =>")]
    [InlineData(Holder + "func run(h: ref/Holder{x}) -> i32\n    let f = func () -> Holder{x} => Holder.init(h.item)\n    return f().peek()\n" + RunTail, "Holder{x} =>")]
    [InlineData(Holder + "let n = 3\nlet h = Holder.init(n@ref)\nlet a1: ref/Holder{w} = h@ref\nlet f = func (g: ref/Holder{w}) -> i32 => g.peek()\nrequire f(h@ref) == 3 else => $abort(\"w\")", "Holder{w}) -> i32")]
    [InlineData(Holder + "func run(h: ref/Holder{x}) -> i32\n    let f: (ref/Holder{x}) -> i32 = func (g) => g.peek()\n    return f(h)\n" + RunTail, "Holder{x}) -> i32 =")]
    [InlineData(Holder + "func size<T>() -> i32 => 3\nfunc run(h: ref/Holder{x}) -> i32\n    let v = size<Holder{x}>()\n    return v\n" + RunTail, "Holder{x}>")]
    [InlineData(Holder + Slotted + "    public computed p: Holder{x}\n        get(self: ref/Self) -> Holder during x => Holder.init(self.item)\n\n    public func get(self: ref/Self) -> i32 => 3\nlet n = 3\nrequire n == 3 else => $abort(\"n\")", "Holder{x}\n")]
    [InlineData(Holder + "func run(h: ref/Holder{x}) -> i32\n    let k: ref/Holder{x} = h\n    return k.peek()\n" + RunTail, "Holder{x} = h")]
    [InlineData(Holder + "func run(h: ref/Holder{x}) -> i32\n    let t: (ref/Holder{x}, i32) = (h, 1)\n    return t.0.peek()\n" + RunTail, "Holder{x}, i32")]
    [InlineData(Holder + Slotted + "    public func get(self: ref/Self) -> i32\n        let j = Holder.init(self.item)\n        let k: ref/Holder{x} = j@ref\n        return k.peek()\nlet n = 3\nlet s = S.init(n@ref)\nrequire s.get() == 3 else => $abort(\"s\")", "Holder{x} = j")]
    [InlineData(Holder + Slotted + "    public func get(self: ref/Self, h: ref/Holder{x}) -> i32 => h.peek()\nlet n = 3\nrequire n == 3 else => $abort(\"n\")", "Holder{x}) -> i32 => h")]
    [InlineData(Holder + Slotted + "    public func make(self: ref/Self) -> Holder{x} => Holder.init(self.item)\nlet n = 3\nrequire n == 3 else => $abort(\"n\")", "Holder{x} =>")]
    [InlineData(Holder + "struct W {x}\n    public let item: ref/i32 during x\n    public let h: Holder{x}\n\n    public init(item: ref/i32 during x) => self.item = item\nlet n = 3\nrequire n == 3 else => $abort(\"n\")", "Holder{x}\n")]
    [InlineData(Holder + "enum E {x}\n    Has(Holder{x})\n    Empty\nlet n = 3\nrequire n == 3 else => $abort(\"n\")", "Holder{x})")]
    [InlineData(Holder + "contract Peeks\n    func get(self: ref/Self, h: ref/Holder{x}, g: ref/Holder{x}) -> i32\nlet n = 3\nrequire n == 3 else => $abort(\"n\")", "Holder{x}) -> i32\n")]
    public void AReusedSetIsADuplicateInEveryPosition(string source, string set)
    {
        var start = source.IndexOf(set, StringComparison.Ordinal);
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics, x => x.Code == nameof(DiagnosticCode.DuplicateBinding_Kd) && x.Span!.Value.Start == start);
        Assert.Equal("declaration", Assert.Single(error.Related!).Role);
    }

    // A new set related to the reused one checks clean in each position, and the grouped and Case forms run (native fixture
    // OriginSetRepeatForms).
    [Theory]
    [InlineData(Holder + "func run(h: ref/Holder{x}) -> i32\n    let k: ref/Holder{x2} = h\n        origin x2.a == x.a\n    return k.peek()\n" + RunTail)]
    [InlineData(Holder + "func run(h: ref/Holder{x}) -> i32\n    let t: (ref/Holder{x2}, i32) = (h, 1)\n        origin x2.a == x.a\n    return t.0.peek()\n" + RunTail)]
    [InlineData(Holder + "func run(h: ref/Holder{x}, g: ref/Holder{y}) -> i32\n    let k: ref/Holder{y2} = h\n        origin y2.a == x.a\n    return k.peek()\nlet n = 3\nlet h = Holder.init(n@ref)\nrequire run(h@ref, h@ref) == 3 else => $abort(\"run\")")]
    [InlineData(Holder + Slotted + "    public func get(self: ref/Self) -> i32\n        let j = Holder.init(self.item)\n        let k: ref/Holder{x2} = j@ref\n            origin x2.a == x\n        return k.peek()\nlet n = 3\nlet s = S.init(n@ref)\nrequire s.get() == 3 else => $abort(\"s\")")]
    [InlineData(Holder + Slotted + "    public func get(self: ref/Self, h: ref/Holder{x2}) -> i32\n        origin x2.a == x\n        return h.peek()\nlet n = 3\nrequire n == 3 else => $abort(\"n\")")]
    [InlineData(Holder + Slotted + "    public func make(self: ref/Self) -> Holder{x2}\n        origin x2.a == x\n        return Holder.init(self.item)\nlet n = 3\nrequire n == 3 else => $abort(\"n\")")]
    [InlineData(Holder + "contract Peeks\n    func get(self: ref/Self, h: ref/Holder{x}, g: ref/Holder{x2}) -> i32\n        origin x2.a == x.a\nlet n = 3\nrequire n == 3 else => $abort(\"n\")")]
    public void ANewRelatedSetChecksCleanInEveryPosition(string source)
    {
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    [Fact]
    public void TheGroupedAndCaseFormsOfAReusedSetRun()
    {
        const string Source = Holder + "struct S {x}\n    public let item: ref/i32 during x\n\n    public init(item: ref/i32 during x) => self.item = item\n\n" +
            "    public func local(self: ref/Self) -> i32\n        let j = Holder.init(self.item)\n        let k: ref/(Holder during x) = j@ref\n        return k.peek()\n\n" +
            "    public func lent(self: ref/Self, h: ref/(Holder during x)) -> i32 => h.peek()\n\n" +
            "    public func make(self: ref/Self) -> Holder during x => Holder.init(self.item)\n\n" +
            "    public func pair(self: ref/Self) -> i32\n        let t: ((Holder during x), i32) = (Holder.init(self.item), 1)\n        return t.0.peek() + t.1\n" +
            "enum E {x}\n    Has(Holder during x)\n    Empty\nenum F {x}\n    Has(Holder{x2})\n        origin x2.a == x\n    Empty\n" +
            "struct W {x}\n    public let item: ref/i32 during x\n    public let h: Holder{x2}\n        origin x2.a == x\n\n    public init(item: ref/i32 during x)\n        self.item = item\n        self.h = Holder.init(item)\n" +
            "let n = 3\nlet s = S.init(n@ref)\nlet j = Holder.init(n@ref)\nlet e = E.Has(Holder.init(n@ref))\nlet f = F.Has(Holder.init(n@ref))\nlet w = W.init(n@ref)\n" +
            "let r = match e\n    .Has(let k) => k.peek()\n    .Empty => 0\nlet q = match f\n    .Has(let k) => k.peek()\n    .Empty => 0\n" +
            "require s.local() == 3 and s.lent(j@ref) == 3 and s.make().peek() == 3 and s.pair() == 4 and r == 3 and q == 3 and w.h.peek() == 3 else => $abort(\"forms\")";
        ScalarEmissionTest.EmitFixture("OriginSetRepeatForms", Source, string.Empty);
    }

    // The counterparts run: a set related by a clause and a slot that a clause relates to static.
    [Theory]
    [InlineData("Related", Holder + "func run(h: ref/Holder{x}) -> i32\n    let viaMethod = h.peek()\n    let d2 = (Holder{y}).peek(h)\n        origin y.a == x.a\n    return viaMethod + d2\nlet n = 3\nlet h = Holder.init(n@ref)\nrequire run(h@ref) == 6 else => $abort(\"run\")")]
    [InlineData("Local", Helpers + "let n = 3\nlet h = Holder.init(n@ref)\nlet e = (Holder{w}).peek(h@ref)\n    origin w.a == h.a\nlet z = (Holder{q}).zero()\n    origin q.a == static\nrequire e == 3 and z == 0 else => $abort(\"local\")")]
    public void ARelatedOrLentQualifierSlotRuns(string name, string source)
        => ScalarEmissionTest.EmitFixture("OriginQualifier" + name, source, string.Empty);

    // The written generic and Slice forms run: a receiver call, a Slice set related to the value's slot, generic Type,
    // generic member and nested sets related to a parameter's Origin, a borrow named by a local, and static.
    [Fact]
    public void ARelatedGenericOrSliceQualifierSlotRuns()
    {
        const string Related = "func related(v: ref/i32 during x) -> i32\n    let u = (View<i32>{p}).twice(v)\n        origin p.source == x\n    let g = (Pin{h}).first(v)\n        origin h.a == x\n    let o = (Outer<i32>.Inner{q}).twice(v)\n        origin q.a == x\n    return u + g + o\n";
        const string Body = Viewed + "let a = view.contains(2@ref)\nlet c = (Slice<i32>{q}).contains(view, 2@ref)\n    origin q.source == view.source\nlet n = 3\nlet r = n@ref\nlet w = (View<i32>{p}).twice(r)\n    origin p.source == r\nlet z = (View<i32>{e}).zero()\n    origin e.source == static\nrequire a and c and w == 6 and z == 0 and related(n@ref) == 17 else => $abort(\"generic\")";
        ScalarEmissionTest.EmitFixture("OriginQualifierGeneric", View + Pin + Outer + Related + Body, string.Empty);
    }

    // SPEC 15.3.1: like a value, a local's binding set is visible only to the declarations after it, so a function's set is not a repeat
    // of a later top-level local's set of the same name (it was a DuplicateBinding_Kd on the function's set); an earlier local's set
    // still is.
    [Fact]
    public void ALaterLocalSetIsNotARepeat()
    {
        const string Run = "func run(h: ref/Holder{x}) -> i32\n    let d = (Holder{p}).peek(h)\n        origin p.a == x.a\n    return d\n";
        const string Body = "let n = 3\nlet h = Holder.init(n@ref)\nlet e = (Holder{p}).peek(h@ref)\n    origin p.a == h.a\nrequire run(h@ref) == 3 and e == 3 else => $abort(\"run\")";
        Assert.Empty(DiagnosticCorpus.Check(Holder + Run + Body).Diagnostics);
        const string Repeated = Holder + "let n = 3\nlet h = Holder.init(n@ref)\nlet e = (Holder{p}).peek(h@ref)\n    origin p.a == h.a\nlet f = (Holder{p}).peek(h@ref)\n    origin p.a == h.a\nrequire e == f else => $abort(\"run\")";
        var error = Assert.Single(DiagnosticCorpus.Check(Repeated).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.DuplicateBinding_Kd), Repeated.LastIndexOf("Holder{p}", StringComparison.Ordinal)), (error.Code, error.Span!.Value.Start));
        Assert.Equal(Repeated.IndexOf("Holder{p}", StringComparison.Ordinal), Assert.Single(error.Related!).Span!.Value.Start);
    }

    [Fact]
    public void ACallThroughTheRequirementStaysSupported()
    {
        const string Source = "func order<T>(a: ref/T, b: ref/T) -> i32\n    T is Comparable\n    return T.compare(a, b)\nlet x = 1\nlet y = 2\nrequire order(x@ref, y@ref) < 0 else => $abort(\"order\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
    }
}
