// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public partial class ImplicitBaseCallTest
{
    private const string LateFailureBehindABaseClause = """
        contract Z
            func z(self: ref/Self) -> i32
        struct Hidden
            Self is Z
            public init() => ()
            public func z(self: ref/Self) -> i32 => 1
        public struct S
            Hidden is Z
            public init() => ()
        open struct Base
            protected init(x: i32 = 0, s: Option<S> = .None) => ()
        struct Leaf: Base
        """;

    private const string LateFailureBehindABaseClauseValid = """
        contract Z
            func z(self: ref/Self) -> i32
        struct Hidden
            Self is Z
            public init() => ()
            public func z(self: ref/Self) -> i32 => 1
        struct S
            Hidden is Z
            public init() => ()
        open struct Base
            protected init(x: i32 = 0, s: Option<S> = .None) => ()
        struct Leaf: Base
        """;

    private const string LimitedBumps = "contract Bumps\n    func bump(self: uniq/Self) -> ()\nopen struct Base0\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base0\n";

    // PLAN G375 (Q13, rule W): RevalidateImplicitConstructors reads a kept Proven, so S's failure after the bodies is seen through its own
    // InaccessibleBinding_Kd records, and the base clause adds no record resting on them.
    [Theory]
    [InlineData(LateFailureBehindABaseClause, "2*InaccessibleBinding_Kd")] // PLAN G1278 (G1278-A, Q13): the default `.None` carries the parameter's Type, so its InvalidConstraint_Kd goes
    [InlineData(LateFailureBehindABaseClauseValid, "clean")]
    public void ARejudgeAfterTheBodiesReadsAKeptProven(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    // PLAN G464 (SPEC 15.6.1, 15.3.4; 6.3.6): a base clause's Type argument holding a Function Type is formed without the nested
    // signature, so the implicit constructor is synthesized (dev: InvalidTypeFormation_Kd at D and UnprovenConstraint_Kd at the base).
    [Theory]
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\nopen struct B<U>\n    public init() => ()\nstruct D {q}: B<(Option<T during q>) -> i32>\nfunc make(s: ref/i32) -> i32\n    let d: D during s = D.init()\n    return 0\n", "clean")] // c1ic
    [InlineData("struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\nopen struct B<U>\n    public init() => ()\nstruct D {q}: B<i32>\nfunc make(s: ref/i32) -> i32\n    let d: D during s = D.init()\n    return 0\n", "clean")] // c1icd
    public void ANestedSignatureInABaseTypeIsNotFormedThere(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    [Fact]
    public void OmittedBaseCallUsesOrdinaryDefaultsBeforeOwnInitializers()
    {
        const string Source = """
            group Helpers
                public func make() -> i32
                    Console.writeLine("default")
                    return 42
                public func own() -> i32
                    Console.writeLine("field")
                    return 7
            open struct Base<T>
                public let item: i32
                protected init(item: i32 = Helpers.make())
                    self.item = item
                    Console.writeLine("base")
                drop => Console.writeLine("base drop")
            open struct Middle<U>: Base<U>
                protected init() => Console.writeLine("middle")
            struct Leaf: Middle<i64>
                public let extra: i32 = Helpers.own()
                public init() => Console.writeLine("leaf")
                drop => Console.writeLine("leaf drop")
            let x = Leaf.init()
            require x.item == 42 and x.extra == 7 else => $abort("initialization")
            """;
        ScalarEmissionTest.EmitFixture("ImplicitBaseDefaults", Source, "default\nbase\nmiddle\nfield\nleaf\nleaf drop\nbase drop\n");
    }

    [Theory]
    [InlineData("protected init(value: i32) => ()", "NoApplicableOverload_Kd")]
    [InlineData("private init() => ()", "NoApplicableOverload_Kd")]
    [InlineData("protected init(value: i32 = 1) => ()\n    protected init(value: string = \"x\") => ()", "AmbiguousBinding_Kd")]
    public void OmissionPreservesBaseSelectionFailures(string declaration, string code)
    {
        var source = "open struct Base\n    " + declaration + "\nstruct Leaf: Base\n    public init() => ()\n()";
        var result = DiagnosticCorpus.Check(source);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(code, record.Code);
        Assert.Equal("init()", source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        Assert.NotEmpty(record.Related!);
        Assert.NotNull(record.Reason);
        var explicitRecord = Assert.Single(DiagnosticCorpus.Check(source.Replace("public init() =>", "public init(): base() =>", StringComparison.Ordinal)).Diagnostics);
        Assert.Equal((record.Code, record.Label, record.Message), (explicitRecord.Code, explicitRecord.Label, explicitRecord.Message));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new(result.Diagnostics, result.Sources), string.Empty);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(result.Sources[record.Source].Path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(result, [identity], identity, related)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
        }
    }

    // SPEC 6.3.6: a derived structure without an explicit constructor whose Fields all have initializers receives init() when its
    // omitted base clause selects a base constructor; it runs that constructor once, then the own initializers, and drops derived first.
    [Theory]
    [InlineData("Public", "open struct Base\n    public var count: i32 = 1\n    public init() => Console.writeLine(\"base\")\nstruct Leaf: Base\n    public let extra: i32 = 2\nlet leaf = Leaf.init()\nrequire leaf.count == 1 and leaf.extra == 2 else => $abort(\"fields\")", "base\n")]
    [InlineData("Implicit", "open struct Base\n    public var count: i32 = 1\nstruct Leaf: Base\n    public let extra: i32 = 2\nlet leaf = Leaf.init()\nConsole.writeLine(\"\\(leaf.count) \\(leaf.extra)\")", "1 2\n")]
    [InlineData("Protected", "open struct Base\n    public var count: i32 = 1\n    protected init() => Console.writeLine(\"base\")\nstruct Leaf: Base\n    public let extra: i32 = 2\nlet leaf = Leaf.init()", "base\n")]
    [InlineData("AllDefault", "open struct Base\n    protected init(x: i32 = 3) => Console.writeLine(\"base \\(x)\")\nstruct Leaf: Base\nlet leaf = Leaf.init()", "base 3\n")]
    [InlineData("OwnedDefault", "open struct Named\n    public let name: string\n    protected init(name: string = \"base\")\n        self.name = name@move\n    drop => Console.writeLine(\"named drop\")\nstruct Entry: Named\n    public let number: i32 = 4\n    drop => Console.writeLine(\"entry drop\")\nlet e = Entry.init()\nConsole.writeLine(\"\\(e.name) \\(e.number)\")", "base 4\nentry drop\nnamed drop\n")]
    [InlineData("Generic", "open struct Base<T>\n    public var count: i32 = 1\n    protected init() => Console.writeLine(\"base\")\nstruct Leaf: Base<i64>\n    public let extra: i32 = 2\nstruct GLeaf<U>: Base<U>\n    public let extra: i32 = 3\nlet l = Leaf.init()\nlet g = GLeaf<i32>.init()\nConsole.writeLine(\"\\(l.extra) \\(g.extra)\")", "base\nbase\n2 3\n")]
    [InlineData("ThreeLevels", "group Order\n    public func mark(name: string) -> i32\n        Console.writeLine(name)\n        return 1\nopen struct A\n    public var a: i32 = Order.mark(\"a\")\n    drop => Console.writeLine(\"drop a\")\nopen struct B: A\n    public var b: i32 = Order.mark(\"b\")\n    drop => Console.writeLine(\"drop b\")\nstruct C: B\n    public let c: i32 = Order.mark(\"c\")\n    drop => Console.writeLine(\"drop c\")\nlet x = C.init()", "a\nb\nc\ndrop c\ndrop b\ndrop a\n")]
    [InlineData("Refuted", "struct NoEq\n    public var v: i32 = 0\nopen struct Base<T>\n    public var count: i32 = 1\n    protected init()\n        T is Equatable\n        Console.writeLine(\"cond\")\n    protected init(x: i32 = 0) => Console.writeLine(\"plain\")\nstruct Leaf: Base<NoEq>\nlet l = Leaf.init()", "plain\n")]
    [InlineData("Chain", "open struct Base\n    public var a: i32 = 1\n    public init() => ()\nopen struct Mid: Base\n    public var b: i32 = 2\nstruct Leaf: Mid\n    public var c: i32 = 3\n    public init() => ()\nlet x = Leaf.init()\nConsole.writeLine(\"\\(x.a + x.b)\")", "3\n")]
    [InlineData("Virtual", "open struct Base\n    public virtual func score(self: objref/Self, bonus: i32 = 1) -> i32\n        effect confined\n        return bonus\nstruct Derived: Base\n    override func score(self: objref/Self, bonus: i32) -> i32\n        return base.score(bonus) + 10\nlet d = Derived.init()@obj\nlet a = d.score()\nlet b = Base.score(d@objref/Base)\nlet operation = Derived.score\nlet c = operation(d@objref/Base, 1)\nrequire a == 11 and b == 11 and c == 11 else => $abort(\"dispatch\")\nConsole.writeLine(\"\\(a)\")", "11\n")]
    public void DerivedStructuresSynthesizeTheirConstructor(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ImplicitBaseDerived" + name, source, stdout);

    // A base default keeps its own location: an Abort it raises is reported where the default is written.
    [Fact]
    public void ASynthesizedConstructorEvaluatesTheBaseDefaults()
        => ScalarEmissionTest.EmitFixture("ImplicitBaseDerivedAbort", "group Helpers\n    public func fail() -> i32 => $abort(\"default\")\nopen struct Base\n    protected init(x: i32 = Helpers.fail()) => ()\nstruct Leaf: Base\nlet leaf = Leaf.init()\nConsole.writeLine(\"after\")", string.Empty, 1, "Hello.kimi:2:34: abort KIMI_E_ABORT: default\n");

    // SPEC 6.3.6: without a selected base constructor a derived structure has no constructor. A construction reports the reason, an
    // Unknown premise that can change the selection, or the base clause failure the decision rests on; an unused Type says nothing, and
    // the decision does not depend on declaration order.
    [Theory]
    [InlineData("open struct Base\n    private init() => ()\nstruct Leaf: Base\nlet leaf = Leaf.init()", "UnresolvedBinding_Kd", "Leaf.init", "Leaf has no constructor: its omitted base clause selects none, since no constructor of Base applies without arguments")]
    [InlineData("open struct Base\n    protected init(a: i32 = 1) => ()\n    protected init(b: string = \"x\") => ()\nstruct Leaf: Base\nlet leaf = Leaf.init()", "UnresolvedBinding_Kd", "Leaf.init", "Leaf has no constructor: its omitted base clause selects none, since more than one constructor of Base applies without arguments")]
    [InlineData("open struct Base\n    public var count: i32\nstruct Leaf: Base\nlet leaf = Leaf.init()", "UnresolvedBinding_Kd", "Leaf.init", "Leaf has no constructor: its omitted base clause selects none, since Base has no constructor")]
    [InlineData("struct NoEq\n    public var v: i32 = 0\nopen struct Base<T>\n    protected init()\n        T is Equatable\n        ()\nstruct Leaf: Base<NoEq>\nlet leaf = Leaf.init()", "UnresolvedBinding_Kd", "Leaf.init", "Leaf has no constructor: its omitted base clause selects none, since no constructor of Base<NoEq> applies without arguments")]
    [InlineData("open struct Base<T>\n    protected init()\n        T is Equatable\n        ()\n    protected init(x: i32 = 0) => ()\nstruct Leaf<U>: Base<U>\nlet leaf = Leaf<i32>.init()", "UnprovenConstraint_Kd", "Leaf<i32>.init", "Leaf<i32> has a synthesized constructor only when the omitted base clause of Leaf<U> selects a constructor of Base<U>")]
    [InlineData("open struct Base<T>\n    protected init()\n        T is Equatable\n        ()\n    protected init(x: i32 = 0) => ()\nopen struct GLeaf<U>: Base<U>\nstruct Leaf: GLeaf<i32>\n    public init() => ()\nlet leaf = Leaf.init()", "UnprovenConstraint_Kd", "init()", "GLeaf<i32> has a synthesized constructor only when the omitted base clause of GLeaf<U> selects a constructor of Base<U>")]
    [InlineData("open struct Base\n    public init() => ()\nstruct Leaf: Base\n    private init() => ()\nlet leaf = Leaf.init()", "NoApplicableOverload_Kd", "Leaf.init()", null)]
    [InlineData("open struct Base\n    private init() => ()\nstruct Leaf: Base\n()", null, null, null)]
    [InlineData("open struct Base\n    protected init(a: i32 = 1) => ()\n    protected init(b: string = \"x\") => ()\nstruct Leaf: Base\n()", null, null, null)]
    [InlineData("open struct Base\n    public var count: i32\nstruct Leaf: Base\n()", null, null, null)]
    [InlineData("open struct Base<T>\n    protected init()\n        T is Equatable\n        ()\n    protected init(x: i32 = 0) => ()\nstruct Leaf<U>: Base<U>\n()", null, null, null)]
    [InlineData("struct Leaf: Base\n    public let extra: i32 = 2\nopen struct Base\n    protected init() => ()\nlet leaf = Leaf.init()", null, null, null)]
    public void ADerivedStructureWithoutASelectedBaseConstructorHasNone(string source, string? code, string? at, string? note)
    {
        var diagnostics = DiagnosticCorpus.Check(source).Diagnostics;
        if (code is null)
        {
            Assert.Empty(diagnostics);
            return;
        }

        var record = Assert.Single(diagnostics);
        Assert.Equal((code, at), (record.Code, source.Substring(record.Span!.Value.Start, record.Span.Value.Length)));
        if (note is not null)
        {
            Assert.StartsWith(note, record.Note, StringComparison.Ordinal);
            Assert.Contains(record.Related!, static x => x.Label == "omitted base clause");
        }
    }

    // SPEC 6.3.6: base clause Constraints that are not Proven leave the decision resting on the clause, whose check reports them; the
    // construction adds no record of its own.
    [Fact]
    public void AnUnsatisfiedBaseClauseLeavesTheConstructionDerived()
    {
        const string Source = "struct NoEq\n    public var v: i32 = 0\nopen struct Base<T>\n    T is Equatable\n    protected init() => ()\nstruct Leaf: Base<NoEq>\nlet leaf = Leaf.init()";
        var diagnostics = DiagnosticCorpus.Check(Source).Diagnostics;
        Assert.Contains(diagnostics, static x => x.Code == nameof(DiagnosticCode.UnsatisfiedConstraint_Kd));
        Assert.DoesNotContain(diagnostics, x => x.Span!.Value.Start >= Source.IndexOf("Leaf.init", StringComparison.Ordinal));
    }

    // The base clause may be written in another source or another fragment of the structure; the synthesized constructor's base call
    // is located at that clause.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheBaseClauseMayBeInAnotherFragment(bool reversed)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        var first = new SourceDocument("leaf.kimi", "struct Leaf\n    public let extra: i32 = 2\nlet leaf = Leaf.init()\nrequire leaf.extra == 2 else => $abort(\"extra\")");
        var second = new SourceDocument("base.kimi", "open struct Base\n    protected init() => ()\nstruct Leaf: Base");
        c.Kotonoha.AddSource(reversed ? second : first);
        c.Kotonoha.AddSource(reversed ? first : second);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var leaf = c.Kotonoha.RootKoto.NestedContainers.OfType<StructKoto>().Single(static x => x.Name == "Leaf");
        var initializer = leaf.ImplicitConstructor!.BaseInitializer!;
        Assert.Equal(("base.kimi", leaf.Bases[0].Span), (initializer.CodeContext.SourceDocument!.Path, initializer.Span));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void SynthesisReusesItsDecisionsWithoutAllocating()
    {
        const string Source = "open struct Base<T>\n    protected init() => ()\n    protected init(x: i32 = 0)\n        T is Equatable\n        ()\nstruct Leaf<U>: Base<U>\n    public let extra: i32 = 2\nstruct Absent: Base<i32>\n    public var other: i32 = 1\nopen struct Closed\n    private init() => ()\nstruct Unused: Closed\nlet leaf = Leaf<i64>.init()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    // SPEC 6.3.6: the omitted base query, which publishes nothing, decides as the bound base call does: the same selected
    // constructor or the same class of failed selection. It leaves the obligations and diagnostics of the pass unchanged, and the
    // implicit twin of each row decides it alike at the declaration pass (AssertOmittedBaseAgreement).
    [Theory]
    [InlineData("open struct Base\n    public init() => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base\n    protected init() => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base\n    private init() => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.NoneApplicable))]
    [InlineData("open struct Base\n    protected init(x: i32 = 1) => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base\n    protected init(x: i32) => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.NoneApplicable))]
    [InlineData("open struct Base\n    protected init() => ()\n    protected init(x: i32 = 0) => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base\n    private init() => ()\n    protected init(x: i32 = 0) => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base\n    protected init(x: i32 = 1) => ()\n    protected init(y: string = \"x\") => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Ambiguous))]
    [InlineData("open struct Base\n    public var count: i32\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.NoBaseConstructor))]
    [InlineData("open struct Base<T>\n    protected init()\n        T is Equatable\n        ()\nstruct Leaf: Base<i32>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base<T>\n    protected init()\n        T is Equatable\n        ()\nstruct Leaf<U>: Base<U>\n    U is Equatable\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base<T>\n    protected init()\n        T is Equatable\n        ()\n    protected init(x: i32 = 0) => ()\nstruct Leaf<U>: Base<U>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Unproven))]
    [InlineData("struct NoEq\n    public var v: i32 = 0\nopen struct Base<T>\n    protected init()\n        T is Equatable\n        ()\n    protected init(x: i32 = 0) => ()\nstruct Leaf: Base<NoEq>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base<T>\n    protected init() => ()\n    protected init(x: i32 = 0)\n        T is Equatable\n        ()\nstruct Leaf<U>: Base<U>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base<T>\n    protected init() => ()\nstruct Leaf: Base<i64>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base {a}\n    public var n: i32 = 1\n    public init() => ()\nstruct Leaf {b}: Base during b\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    // SPEC 23.1.2: a base constructor whose premise rests on a conformance limited by OCC-X leaves the selection resting on that
    // conformance.
    [InlineData(LimitedBumps + "open struct Holder<T>\n    protected init()\n        T is Bumps and Owned\n        ()\nstruct Derived: Holder<Leaf>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Dependent))]
    [InlineData(LimitedBumps + "open struct Holder<T>\n    protected init()\n        T is not Bumps\n        ()\nstruct Derived: Holder<Leaf>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Dependent))]
    [InlineData(LimitedBumps + "struct GLeaf<U>: Base0\n    var item: U\nopen struct Holder<T>\n    protected init()\n        T is Bumps and Owned\n        ()\nstruct Derived<U>: Holder<GLeaf<U>>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Dependent))]
    [InlineData(LimitedBumps + "open struct Holder<T>\n    protected init() => ()\n    protected init(x: i32 = 0)\n        T is Bumps\n        ()\nstruct Derived: Holder<Leaf>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Dependent))] // Selected before R1f (STATUS).
    public void OmittedBaseQueryAgreesWithTheBoundCall(string source, string outcome)
        => AssertOmittedBaseAgreement(source, outcome, static x => CompilationTestHelper.ParseSuccess(x));

    [Theory]
    [InlineData("internal", nameof(OmittedBaseOutcome.NoneApplicable))]
    [InlineData("protected internal", nameof(OmittedBaseOutcome.Selected))]
    public void OmittedBaseQueryUsesTheDerivedModulesAccess(string access, string outcome)
        => AssertOmittedBaseAgreement("alias Lib.Api\nstruct Leaf: Base\n    public init() => ()\n()", outcome, x => ModuleBindingTest.Create(x, "public group Api\n    public open struct Base\n        " + access + " init() => ()"));

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedOmittedBaseQueriesAllocateNothing()
    {
        var c = CompilationTestHelper.ParseSuccess("open struct Base<T>\n    protected init() => ()\n    protected init(x: i32 = 0)\n        T is Equatable\n        ()\nstruct Leaf<U>: Base<U>\n    public init() => ()\n()");
        Assert.True(c.Bind().IsComplete);
        var constructor = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.HasOmittedBaseInitializer);
        var selected = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => selected &= c.Binding.SelectOmittedBaseConstructor(constructor).Outcome == OmittedBaseOutcome.Selected));
        Assert.True(selected);
    }

    // SPEC 6.3.6: a base without constructors is one Language absence at the base call target, omitted or written, placed alike by the
    // CLI and the language server, whether the base lacks an initializer or its own omitted base clause selects no constructor.
    [Theory]
    [InlineData("open struct Base\n    public var count: i32\n", "Base has no constructor: its Field count has no initializer")]
    [InlineData("open struct Root\n    private init() => ()\nopen struct Base: Root\n    public var count: i32 = 1\n", "Base has no constructor: its omitted base clause selects none, since no constructor of Root applies")]
    public void ABaseWithoutConstructorsIsOneLocatedProblem(string bases, string note)
    {
        const string Leaf = "struct Leaf: Base\n    public init() => ()\n()";
        foreach (var written in new[] { false, true })
        {
            var source = bases + (written ? Leaf.Replace("public init() =>", "public init(): base() =>", StringComparison.Ordinal) : Leaf);
            var result = DiagnosticCorpus.Check(source);
            var record = Assert.Single(result.Diagnostics);
            Assert.Equal((nameof(DiagnosticCode.UnresolvedBinding_Kd), written ? "base" : "init()"), (record.Code, source.Substring(record.Span!.Value.Start, record.Span.Value.Length)));
            Assert.StartsWith(note, record.Note, StringComparison.Ordinal);

            var identity = SourceIdentity.FromPath(result.Sources[record.Source].Path);
            var sent = Assert.Single(WorkspaceCheck.Place(result, [identity], identity, true)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
        }
    }

    // SPEC 8.3.3: an Unknown premise of a base constructor defers the omitted and the written base call alike, and only when it can
    // affect the selection: the plain init() beats a conditional one that uses a default, and a better conditional one stays unproven.
    [Theory]
    [InlineData("protected init() => ()\n    protected init(x: i32 = 0)\n        T is Equatable\n        ()", null)]
    [InlineData("protected init()\n        T is Equatable\n        ()\n    protected init(x: i32 = 0) => ()", "UnprovenConstraint_Kd")]
    public void AnUnknownBasePremiseDefersOnlyASelectionItCanAffect(string declarations, string? code)
    {
        var source = "open struct Base<T>\n    public var count: i32 = 1\n    " + declarations + "\nstruct Leaf<U>: Base<U>\n    public init() => ()\n()";
        foreach (var written in new[] { false, true })
        {
            var result = DiagnosticCorpus.Check(written ? source.Replace("public init() =>", "public init(): base() =>", StringComparison.Ordinal) : source);
            Assert.Equal(code is null ? [] : [code], result.Diagnostics.Select(static x => x.Code));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(": base()")]
    public void AccessibleZeroArgumentBaseHasOneInvocation(string clause)
    {
        var source = "open struct Base\n    protected init() => Console.writeLine(\"base\")\nstruct Leaf: Base\n    public init()" + clause + " => ()\nlet x = Leaf.init()";
        ScalarEmissionTest.EmitFixture("ImplicitBaseSingle" + clause.Length, source, "base\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RebindingReusesTheImplicitCallWithoutChangingWrittenSyntax()
    {
        const string Source = "open struct Base\n    protected init(value: i32 = 42) => ()\nstruct Leaf: Base\n    public init() => ()\nlet x = Leaf.init()";
        var c = MinimalEmissionTest.Analyze(Source);
        var constructor = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Last(x => x.IsConstructor);
        var initializer = constructor.BaseInitializer;
        Assert.NotNull(initializer);
        Assert.DoesNotContain(": base", constructor.ToString(), StringComparison.Ordinal);
        Assert.Equal(constructor.SignatureSpan, initializer.Span);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.Same(initializer, constructor.BaseInitializer);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    // The query of the bound pass's explicit constructor, then its implicit twin: the derived structure without that constructor,
    // whose synthesized constructor the declaration pass decides by the same query (CompleteImplicitConstructor) and the pass judges
    // again after the bodies, failing closed at the base clause on a disagreement (RevalidateImplicitConstructors). The twin's decision
    // is seen at its use: a Selected one is the synthesized constructor and its bound base call, a Dependent one a use resting on its
    // cause, any other the construction's failure.
    private static void AssertOmittedBaseAgreement(string source, string expected, Func<string, Compilation> create)
    {
        const string Explicit = "\n    public init() => ()";
        var outcome = Enum.Parse<OmittedBaseOutcome>(expected);
        var c = create(source);
        c.Bind();
        var constructor = c.SourceModules.SelectMany(static x => KotoTree.Walk(x.RootKoto)).OfType<FunctionKoto>().Single(static x => x.HasOmittedBaseInitializer);
        var call = constructor.BaseInitializer!;
        var (issues, obligations, state) = (c.Binding.Issues.Count, c.Binding.Obligations.Count, (call.StateOf(), call.FailureOf(), call.TypeOf()));
        var query = c.Binding.SelectOmittedBaseConstructor(constructor);
        (OmittedBaseOutcome Outcome, FunctionKoto? Winner) bound = call.CallOf() is { } selected ? (OmittedBaseOutcome.Selected, (FunctionKoto?)selected.Target.Declaration)
            : call.Method.FailureOf() == BindingFailure.MissingName ? (OmittedBaseOutcome.NoBaseConstructor, null)
            : (call.FailureOf() switch
            {
                BindingFailure.NoApplicableCandidate => OmittedBaseOutcome.NoneApplicable,
                BindingFailure.Ambiguous => OmittedBaseOutcome.Ambiguous,
                BindingFailure.UnprovenConstraint => OmittedBaseOutcome.Unproven,
                BindingFailure.None => OmittedBaseOutcome.Dependent,
                var failure => throw new InvalidOperationException($"The bound base call failed with {failure}."),
            }, null);
        Assert.Equal((outcome, bound.Outcome, bound.Winner), (query.Outcome, query.Outcome, query.Winner));

        // A query inside the finished pass changes nothing the pass published.
        Assert.Equal((issues, obligations, state), (c.Binding.Issues.Count, c.Binding.Obligations.Count, (call.StateOf(), call.FailureOf(), call.TypeOf())));

        var structure = (StructKoto)constructor.Parent!;
        var arguments = structure.GenericParameterNodes.Count == 0 ? string.Empty : "<" + string.Join(", ", Enumerable.Repeat("i32", structure.GenericParameterNodes.Count)) + ">";
        var twin = create(source.Remove(source.LastIndexOf(Explicit, StringComparison.Ordinal), Explicit.Length) + (outcome == OmittedBaseOutcome.Selected ? string.Empty : $"\nlet twin = {structure.Name}{arguments}.init()"));
        twin.Bind();
        var derived = twin.SourceModules.SelectMany(static x => KotoTree.Walk(x.RootKoto)).OfType<StructKoto>().Single(x => x.Name == structure.Name);
        Assert.Equal(BindingFailure.None, derived.BaseClause!.FailureOf());
        if (outcome == OmittedBaseOutcome.Selected)
        {
            var synthesized = derived.ImplicitConstructor!;
            var decision = twin.Binding.SelectOmittedBaseConstructor(synthesized);
            Assert.Equal((outcome, query.Winner!.Span), (decision.Outcome, decision.Winner!.Span));
            Assert.Same(decision.Winner, synthesized.BaseInitializer!.CallOf()!.Target.Declaration);
            return;
        }

        var use = KotoTree.Walk(derived.Parent!).OfType<InvocationKoto>().Last();
        Assert.Null(derived.ImplicitConstructor);
        Assert.Null(use.CallOf());
        if (outcome == OmittedBaseOutcome.Dependent)
        {
            // The use rests on the decision's cause and adds no record of its own: the twin's records are the row's.
            Assert.Equal(c.Binding.Issues.Select(static x => (x.Code, x.Node.ToString())), twin.Binding.Issues.Select(static x => (x.Code, x.Node.ToString())));
            return;
        }

        Assert.Equal(outcome == OmittedBaseOutcome.Unproven ? BindingFailure.UnprovenConstraint : BindingFailure.MissingName, use.Method.FailureOf());
    }
}
