// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Kimi;
using Xunit;

namespace XunitTest;

// SPEC 6.2 Finite inline storage: a declaration whose storage reaches the same declaration again by value, with any Type arguments, is
// a Language error of the declaration (InvalidInlineLayout_Kd, DECISIONS BU-65); exceeding the inline nesting depth bound is a resource
// limit (IMPL 21.3.2).
public class InlineLayoutTest
{
    private const string Box = "struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value\n";
    private const string BindingCycle = "contract C\n    associate Item\nstruct Box<T>\n    T is C\n    public var next: T.(C).Item\nstruct S\n    Self is C\n";
    private const string BorrowSlot = "contract C\n    associate Item\nstruct Box<T> {s}\n    T is C\n";
    private const string M2Records = "InvalidTypeFormation_Kd struct | UnsatisfiedConstraint_Kd B<Box<D>> | InvalidConstraint_Kd B<Box<D>> | InvalidConstraint_Kd Box<D> | InvalidConstraint_Kd Box<D>";
    private const string T08Records = "InvalidTypeFormation_Kd struct | UnprovenConstraint_Kd B<Box<D>> | InvalidConstraint_Kd Box<D> | InvalidConstraint_Kd Box<D>";
    private const string Heir = "contract C\n    associate Item\nopen struct B<X>\n    Self is C\n    associate C.Item is X\nstruct Box<T>\n    T is C\n    public var next: T.(C).Item\n";
    private const string TwoReferences = "contract C<U>\n    associate Item\nstruct Box<T>\n    T is C<i32>\n    public var next: Option<T.(C<i32>).Item>\nstruct S\n    Self is C<i32>\n    Self is C<i64>\n";

    [Theory]
    [InlineData("struct Node\n    let value: i32\n    let next: Node\n", "Node")]
    [InlineData("struct A\n    let b: B\nstruct B\n    let a: A\n", "A,B")]
    [InlineData(Box + "struct E\n    let b: Box<E>\n", "E")]
    [InlineData("struct Tree\n    let left: Option<Tree>\n", "Tree")]
    [InlineData("struct Pair\n    let items: [2 of (i32, Pair)]\n", "Pair")]
    [InlineData("enum E\n    Empty\n    Again(E)\nlet x = E.Empty", "E")]
    [InlineData("enum E<T>\n    Empty\n    Again(E<E<T>>)\nlet x = E<i32>.Empty", "E")]
    [InlineData("enum A\n    Empty\n    Again(B)\nenum B\n    Again(A)\nlet x = A.Empty", "A,B")]

    [InlineData("struct R\n    let r: C<u8>\nstruct C<T>\n    let t: (C<i32>, u8)\n", "C")]

    // PLAN G310 (DECISIONS BU-65): R2<T> reaches R2<i32> through M, so M and R2 lie on one cycle, whatever A stores (before BU-65 an
    // instance walk marked M finite when its second visit added no new member, and R2 went unreported).
    [InlineData("struct A\n    let p: (M, M)\nstruct R2<T>\n    let m: M\nstruct M\n    let r: R2<i32>\n", "M,R2")]
    public void SelfContainingDeclarationsAreDiagnosed(string source, string cyclic)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        var diagnosed = c.Binding.Issues.Where(x => x.Code == DiagnosticCode.InvalidInlineLayout_Kd).Select(static x => x.Node).OfType<Kimi.Compiler.Parsing.DeclarationContainerKoto>().Select(static x => x.Name).Order(StringComparer.Ordinal);
        Assert.Equal(cyclic.Split(','), diagnosed);

        // DECISIONS R5-M37: Binding publishes the same declarations as AdtDef.InlineCycle, Layout's Rejected storage.
        var published = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.DeclarationContainerKoto>()
            .Where(static x => x.SymbolOf()?.Adt is { InlineCycle: true }).Select(static x => x.Name).Order(StringComparer.Ordinal);
        Assert.Equal(cyclic.Split(','), published);
    }

    // PLAN G310 (DECISIONS BU-65): one verdict in every declaration order (all orders up to four declarations, else each rotation of the
    // declarations and of their reverse): the declarations on a cycle of the inline edges and no other.
    [Theory]
    [InlineData("A/R2/M")]
    [InlineData("A/R2/M single")]
    [InlineData("R/C")]
    [InlineData("F2 argument")]
    [InlineData("F3 Option")]
    [InlineData("F3 Box")]
    [InlineData("F4 growth")]
    [InlineData("recursive Contract")]
    [InlineData("binding")]
    [InlineData("conditional binding")]
    [InlineData("conditional binding finite")]
    [InlineData("inherited binding")]
    [InlineData("two-step binding")]
    [InlineData("pair target")]
    public void EveryDeclarationOrderGivesOneVerdict(string form)
    {
        var (declarations, cyclic) = OrderForms[form];
        int? first = null;
        foreach (var order in Orders(declarations.Length))
        {
            var c = MirBuildTest.Compile(string.Concat(order.Select(i => declarations[i])) + "public func main() => ()\n");
            var layouts = c.Binding.Issues.Where(static x => x.Code == DiagnosticCode.InvalidInlineLayout_Kd).ToArray();
            var diagnosed = layouts.Select(static x => x.Node).OfType<Kimi.Compiler.Parsing.DeclarationContainerKoto>().Select(static x => x.Name).Order(StringComparer.Ordinal);
            var published = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.DeclarationContainerKoto>()
                .Where(static x => x.SymbolOf()?.Adt is { InlineCycle: true }).Select(static x => x.Name).Order(StringComparer.Ordinal);
            Assert.Equal((string.Join(',', order), cyclic), (string.Join(',', order), string.Join(',', diagnosed)));
            Assert.Equal(cyclic, string.Join(',', published));
            Assert.True(cyclic.Length != 0 || c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));

            // SPEC 6.2, the owner's O-70 (a): a binding on the cycle reports at its site too, as many in every order.
            var sites = layouts.Count(static x => x.Node is not Kimi.Compiler.Parsing.DeclarationContainerKoto);
            first ??= sites;
            Assert.Equal((string.Join(',', order), first.Value), (string.Join(',', order), sites));
        }
    }

    // SPEC 6.2, the owner's O-70 (a) (DECISIONS BU-128): an associated binding whose storage reaches itself is invalid at the binding,
    // used or not: its specification (the clause whose conjunction yields its identity), else the conformance clause where it is inferred;
    // a Language record, never a Resource one. A declaration on the cycle reports too; a binding that only reaches the cycle does not. A
    // borrow, raw pointer, object handle or sequence is an indirection. VerifyConformance still forms each binding (S3, R7-C2 (b)), so the
    // valid twins keep that Proof record until then and get no layout record.
    [Theory]
    [InlineData(BindingCycle + "    associate C.Item is Box<S>\n", "Library", "C.Item is Box<S>")]
    [InlineData(BindingCycle + "    associate C.Item is (i32, Option<Box<S>>)\n", "Library", "C.Item is (i32, Option<Box<S>>)")]
    [InlineData(BindingCycle + "    associate C.Item is Option<Box<S>>\nlet b: Option<Box<S>> = .None\nConsole.writeLine(\"x\")\n", "Application", "C.Item is Option<Box<S>>")]
    [InlineData("contract C\n    associate Item\n    func get(self: ref/Self) -> Self.Item\nstruct Box<T>\n    T is C\n    public var next: (i32, T.(C).Item)\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S> => $abort(\"x\")\n", "Library", "Self is C")]
    [InlineData("contract K\ncontract C\n    associate Item\nstruct Box<T>\n    T is C\n    public var next: T.(C).Item\nstruct S\n    Self is C\n    associate C.Item is K\n    associate C.Item is Box<S>\n", "Library", "C.Item is Box<S>")]
    [InlineData("contract C\n    associate A\n    associate B\nstruct Box<T>\n    T is C\n    public var next: T.(C).A\nstruct S\n    Self is C\n    associate C.A is Box<S>\n    associate C.B is Box<S>\n", "Library", "C.A is Box<S>")]
    [InlineData("contract C\n    associate A\n    associate B\nstruct Box<T>\n    T is C\n    public var next: T.(C).B\nstruct S\n    Self is C\n    associate C.A is Box<S>\n    associate C.B is Option<S.(C).A>\n", "Library", "C.B is Option<S.(C).A>")]
    [InlineData("contract C\n    associate Item\nstruct Box<T>\n    T is C\n    public var next: Option<T.(C).Item>\nstruct S\n    Self is C\n    associate C.Item is Box<R>\nstruct R\n    Self is C\n    associate C.Item is Box<S>\n", "Library", "C.Item is Box<R> | C.Item is Box<S>")]
    [InlineData("contract C\n    associate Item\nstruct Wrap<U>\n    public var u: U\nstruct Box<T>\n    T is C\n    public var next: Wrap<T.(C).Item>\nstruct S\n    Self is C\n    associate C.Item is Box<S>\n", "Library", "C.Item is Box<S>")]
    [InlineData("contract C\n    associate Item\nstruct Box<T>\n    T is C\n    public var next: [0 of T.(C).Item]\nstruct S\n    Self is C\n    associate C.Item is Box<S>\n", "Library", "C.Item is Box<S>")]
    [InlineData("contract C\n    associate Item\nenum E<T>\n    T is C\n    Leaf\n    Node(T.(C).Item)\nstruct S\n    Self is C\n    associate C.Item is E<S>\n", "Library", "C.Item is E<S>")]
    [InlineData("contract C\n    associate Item\nstruct Box<T>\n    T is C\n    public var next: Option<T.(C).Item>\nstruct G<X>\n    Self is C\n    associate C.Item is Box<G<X>>\n", "Library", "C.Item is Box<G<X>>")]
    [InlineData("contract K\ncontract C\n    associate Item\nstruct Box<T>\n    T is C\n    public var next: Option<T.(C).Item>\nstruct H<X>\n    Self is C when X is K\n        associate C.Item is Box<H<X>>\n", "Library", "C.Item is Box<H<X>>")]
    [InlineData("contract Has2\n    associate Out\nstruct W<X>\n    Self is Has2\n    associate Has2.Out is PH<W<(X, X)>>\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\n", "Library", "Has2.Out is PH<W<(X, X)>>")]
    [InlineData("contract C\n    associate Item\nstruct Box<T>\n    T is C\n    public var next: Option<T.(C).Item>\nstruct Wrap\n    public var b: Box<S>\nstruct S\n    Self is C\n    associate C.Item is Wrap\n", "Library", "struct | C.Item is Wrap")]
    [InlineData("contract C\n    associate Item\nstruct W\n    let s: S.(C).Item\nstruct S\n    Self is C\n    associate C.Item is Option<W>\n", "Library", "struct")]
    [InlineData("contract K\ncontract C\n    associate Item\nstruct Box<T>\n    T is C\n    public var next: Option<T.(C).Item>\nstruct H<X>\n    Self is C when X is K\n        associate C.Item is K\n        associate C.Item is Box<H<X>>\n", "Library", "C.Item is Box<H<X>>")]
    [InlineData("contract C\n    associate Item\nstruct Box<T>\n    T is C\n    public var next: Option<T.(C).Item>\nstruct S\n    Self is C\n    associate C.Item is Box<S> and Copy\n", "Library", "C.Item is Box<S> and Copy")]
    [InlineData(TwoReferences + "    associate C<i64>.Item is Box<S>\n    associate C<i32>.Item is Box<S>\n", "Library", "C<i32>.Item is Box<S>")]
    [InlineData(TwoReferences + "    associate C<i32>.Item is Box<S>\n    associate C<i64>.Item is Box<S>\n", "Library", "C<i32>.Item is Box<S>")]
    [InlineData(BindingCycle + "    associate C.Item is Array<Box<S>>\n", "Library", "")]
    [InlineData(BindingCycle + "    associate C.Item is Option<rc/Box<S>>\n", "Library", "")]
    [InlineData(BindingCycle + "    associate C.Item is raw/Box<S>\n", "Library", "")]
    [InlineData(BindingCycle + "    associate C.Item is ref/Box<S> during static\n", "Library", "")]
    [InlineData(BorrowSlot + "    let next: ref/T.(C).Item during s\nstruct S\n    Self is C\n    associate C.Item is Box<S> during static\n", "Library", "")]
    [InlineData(BorrowSlot + "    let next: T.(C).Item\n    let r: ref/i32 during s\nstruct S\n    Self is C\n    associate C.Item is Box<S> during static\n", "Library", "C.Item is Box<S> during static")]
    public void ABindingOnItsOwnInlineCycleIsReportedAtTheBinding(string source, string outputKind, string sites)
    {
        var check = DiagnosticCorpus.Check(source, outputKind);
        var layouts = check.Diagnostics.Where(static x => x.Code == nameof(DiagnosticCode.InvalidInlineLayout_Kd)).Select(x => x.Span is { } at ? source.Substring(at.Start, at.Length) : string.Empty);
        Assert.Equal(sites, string.Join(" | ", layouts));
        Assert.DoesNotContain(check.Diagnostics, static x => x.Code is nameof(DiagnosticCode.GenerationResourceLimit_Kd) or nameof(DiagnosticCode.InternalInvariant_Kd));
    }

    // SPEC 6.2, 8.6.2 (PLAN G631): an inherited binding whose storage reaches it again only under the heir's base arguments is invalid at
    // the heir's conformance, its base clause, once, used or not, through any number of bases, nested or transformed arguments, mutual or
    // generic heirs, a fixed array or a refinement. A base clause that fails by its own checks keeps their records whether or not the cycle
    // is there (m2: B's premise refuted; t08: SPEC 8.3.3; their twins store the projection in an Array): the record is written after them.
    // A later writer at an inherited path's Use (ValidateBoundConformanceAgreement, SPEC 8.9.2: G<Z> and G<i32> disagree at Z = i32)
    // still reports there.
    [Theory]
    [InlineData(Heir + "struct D : B<Box<D>>\n", "InvalidInlineLayout_Kd B<Box<D>>")]
    [InlineData("contract C\n    associate Item\nopen struct B<X>\n    Self is C\n    associate C.Item is X\nstruct Box<T>\n    T is C\n    public var next: Option<T.(C).Item>\nstruct D : B<Box<D>>\nfunc use(x: ref/D.(C).Item, y: ref/Box<D>) -> i32 => 1\n", "InvalidInlineLayout_Kd B<Box<D>>")]
    [InlineData(Heir + "struct D : B<Option<(i32, Box<D>)>>\n", "InvalidInlineLayout_Kd B<Option<(i32, Box<D>)>>")]
    [InlineData(Heir + "open struct M<Y> : B<Y>\nstruct D : M<Box<D>>\n", "InvalidInlineLayout_Kd M<Box<D>>")]
    [InlineData(Heir + "struct E<Z> : B<Box<E<Z>>>\n", "InvalidInlineLayout_Kd B<Box<E<Z>>>")]
    [InlineData(Heir + "open struct M<Y> : B<Y>\nopen struct N<W> : M<W>\nstruct D : N<Box<D>>\n", "InvalidInlineLayout_Kd N<Box<D>>")]
    [InlineData(Heir + "open struct M<Y> : B<Option<Y>>\nstruct D : M<Box<D>>\n", "InvalidInlineLayout_Kd M<Box<D>>")]
    [InlineData(Heir + "open struct M<Y> : B<(i32, Y)>\nopen struct N<W> : M<Option<W>>\nstruct D : N<Box<D>>\n", "InvalidInlineLayout_Kd N<Box<D>>")]
    [InlineData(Heir + "struct D1 : B<Box<D2>>\nstruct D2 : B<Box<D1>>\n", "InvalidInlineLayout_Kd B<Box<D2>> | InvalidInlineLayout_Kd B<Box<D1>>")]
    [InlineData(Heir + "struct E<Z> : B<Box<E<Option<Z>>>>\n", "InvalidInlineLayout_Kd B<Box<E<Option<Z>>>>")]
    [InlineData("contract C\n    associate Item\nopen struct B<X>\n    Self is C\n    associate C.Item is X\nstruct Box<T>\n    T is C\n    public var next: [1 of T.(C).Item]\nstruct D : B<Box<D>>\n", "InvalidInlineLayout_Kd B<Box<D>>")]
    [InlineData("contract C\n    associate Item\ncontract C3: C\nopen struct B<X>\n    Self is C3\n    associate C.Item is X\nstruct Box<T>\n    T is C\n    public var next: T.(C).Item\nstruct D : B<Box<D>>\n", "InvalidInlineLayout_Kd B<Box<D>>")]
    [InlineData("contract C\n    associate Item\ncontract K2\nopen struct B<X>\n    X is K2\n    Self is C\n    associate C.Item is X\nstruct Box<T>\n    T is C\n    public var next: T.(C).Item\nstruct D : B<Box<D>>\n", M2Records)]
    [InlineData("contract C\n    associate Item\ncontract K2\nopen struct B<X>\n    X is K2\n    Self is C\n    associate C.Item is X\nstruct Box<T>\n    T is C\n    public var next: Array<T.(C).Item>\nstruct D : B<Box<D>>\n", M2Records)]
    [InlineData("contract C\n    associate Item\ncontract K\nopen struct B<X>\n    Self is C when X is K\n        associate C.Item is X\nstruct Box<T>\n    T is C\n    Self is K\n    public var next: T.(C).Item\nstruct D : B<Box<D>>\n", T08Records)]
    [InlineData("contract C\n    associate Item\ncontract K\nopen struct B<X>\n    Self is C when X is K\n        associate C.Item is X\nstruct Box<T>\n    T is C\n    Self is K\n    public var next: Array<T.(C).Item>\nstruct D : B<Box<D>>\n", T08Records)]
    [InlineData("contract G<T>\n    associate Item\nopen struct B<X>\n    Self is G<X>\n    associate G<X>.Item is i32\nstruct E<Z> : B<Z>\n    Self is G<i32>\n    associate G<i32>.Item is bool\n", "IncompatibleContractImplementation_Kd B<Z> | InvalidConstraint_Kd Self is G<i32>")]
    public void AnInheritedBindingOnItsInlineCycleIsReportedAtTheHeirsBaseClause(string source, string records)
    {
        var check = DiagnosticCorpus.Check(source, "Library");
        Assert.Equal(records, string.Join(" | ", check.Diagnostics.Select(x => x.Code + " " + (x.Span is { } at ? source.Substring(at.Start, at.Length) : string.Empty))));
    }

    [Fact]
    public void IndirectionsAndFiniteNestingAreAccepted()
    {
        // A reference layer, an object layer and a Slice are indirections; by-value nesting of distinct Types is finite.
        var c = MinimalEmissionTest.Analyze(Box + "struct Pair\n    let a: Box<i32>\n    let b: (i32, [2 of bool])\n    public init(a: Box<i32>, b: (i32, [2 of bool])) => (self.a, self.b) = (a, b)\nstruct View {source}\n    let items: Slice<View>{source}\nlet p = Pair.init(Box<i32>.init(1), (2, [true, false]))");
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidInlineLayout_Kd);
    }

    [Fact]
    public void NestingBeyondTheDepthBoundIsAResourceLimit()
    {
        var source = new StringBuilder("struct S0\n    let v: i32\n");
        for (var i = 1; i <= 70; i++)
        {
            source.Append("struct S").Append(i).Append("\n    let v: S").Append(i - 1).Append('\n');
        }

        source.Append("func f(s: S70) -> i32 => 1\nConsole.writeLine(\"deep\")\n");
        MinimalEmissionTest.AssertGenerationResourceLimit(source.ToString(), "S70");
    }

    [Fact]
    public async Task ABodyResolvingNestedTypesBottomUpReportsTheDepthLimit()
    {
        // DECISIONS R5-M37 (a declared change of P7c): the old pool resolves each Type once per body, so t65's one-component Tuple,
        // 65 levels high, reused t64's resolution and was generated, while a fresh resolution reports the depth limit; Layout's
        // placement height now decides it whatever the order. Check reports it once per erased Type, at t65 to t70, the locals whose Types
        // are more than 64 levels high (K13b, DECISIONS R5-M44, R5-M156).
        var source = new StringBuilder("func f() -> i32\n    let t0 = 1\n");
        for (var i = 1; i <= 70; i++)
        {
            source.Append("    let t").Append(i).Append(" = (t").Append(i - 1).Append(",)\n");
        }

        source.Append("    return 0\nlet r = f()\nConsole.writeLine(\"deep\")\n");
        var directory = Path.Combine(Path.GetTempPath(), "kimi-inline-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var project = new Project(new Kimigayo(console)) { Name = "layout", Directory = directory };
            project.AddSource("main.kimi", source.ToString());
            Assert.False(await project.Generate(TestContext.Current.CancellationToken));
            Assert.Contains("GenerationResourceLimit_Kd", console.Text, StringComparison.Ordinal);
            var records = Regex.Matches(console.Text, @"GenerationResourceLimit_Kd\n --> (?<fact>\S+)").Select(static x => x.Groups["fact"].Value);
            var located = string.Join(' ', Enumerable.Range(67, 6).Select(static x => $"main.kimi:{x}:5"));
            Assert.Equal(located, string.Join(' ', records));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("[2147483648 of u8]")]
    [InlineData("[1073741824 of i32]")]
    [InlineData("(bool, [2147483647 of u8])")]
    public void FiniteLayoutsBeyondTheInternalSizeOrCountBoundAreResourceLimits(string type)
        => MinimalEmissionTest.AssertGenerationResourceLimit($"func inspect(value: {type}) -> i32 => 1\nConsole.writeLine(\"finite\")", type);

    [Fact]
    public async Task AnInferredCLayoutInstanceReportsALaterFieldsResourceLimit()
    {
        // SPEC 8.15.3 (DECISIONS R4-M134 Q8): the inferred instance's zero-sized Field is InvalidCLayout_Kd at check (the
        // checked set's layout obligations), before code generation could see the later Field's resource limit.
        var directory = Path.Combine(Path.GetTempPath(), "kimi-inline-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var project = new Project(new Kimigayo(console)) { Name = "layout", Directory = directory };
            project.AddSource("main.kimi", "#Layout(\"C\")\nstruct P<T>\n    var value: T\n    var big: [2147483648 of u8]\nfunc f<T>(value: T) -> i32\n    let x: Option<P<T>> = .None\n    return 0\nlet r = f(())\nConsole.writeLine(\"inferred\")");
            Assert.False(await project.Generate(TestContext.Current.CancellationToken));
            Assert.Contains("InvalidCLayout_Kd", console.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("GenerationResourceLimit_Kd", console.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("InternalInvariant_Kd", console.Text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void EnumTagAndAlignmentCountTowardsTheSizeLimit()
        => MinimalEmissionTest.AssertGenerationResourceLimit("enum Big\n    A([2147483647 of u8])\n    B\nfunc inspect(value: Big) -> i32 => 1\nConsole.writeLine(\"finite\")", "Big");

    private const string Src = "contract Src\n    associate Item\n";
    private const string Src2 = "contract Src2\n    associate Val\n";
    private const string StoresItem = "struct X<T>\n    T is Src\n    let b: T.Item\n";
    private const string Conditional = "    Self is Src when J is Src2\n        associate Src.Item is J.Val\n";
    private const string ValIsD = "struct S2\n    Self is Src2\n    associate Src2.Val is D\n";

    // Each form's declarations and the declarations on a cycle.
    private static readonly Dictionary<string, (string[] Declarations, string Cyclic)> OrderForms = new()
    {
        ["A/R2/M"] = (["struct A\n    let p: (M, M)\n", "struct R2<T>\n    let m: M\n", "struct M\n    let r: R2<i32>\n"], "M,R2"),
        ["A/R2/M single"] = (["struct A\n    let p: M\n", "struct R2<T>\n    let m: M\n", "struct M\n    let r: R2<i32>\n"], "M,R2"),
        ["R/C"] = (["struct R\n    let r: C<i32>\n", "struct C<T>\n    let t: (C<i32>, u8)\n"], "C"),

        // An argument stored inline is no cycle of the generic declaration: X<u8> under D never meets X<D> (before BU-65 D was rejected in
        // three orders).
        ["F2 argument"] = (["struct R\n    let r: X<D>\n", "struct X<T>\n    let t: T\n", "struct D\n    let x: X<u8>\n"], string.Empty),

        // Nested instances of one generic declaration are no indirection (before BU-65 these infinite layouts were accepted).
        ["F3 Option"] = (["struct Node\n    let next: Option<Option<Node>>\n"], "Node"),
        ["F3 Box"] = (["struct R\n    let x: Box<Box<D>>\n", "struct D\n    let r: R\n", "struct Box<T>\n    let t: T\n"], "D,R"),

        // D and G reach themselves through E's growth, D -> E<i32> -> E<G<i32>> -> G<i32> -> D (before BU-65 only E was reported).
        ["F4 growth"] = (["struct E<T>\n    let t: T\n    let x: E<G<T>>\n", "struct G<T>\n    let d: D\n", "struct D\n    let e: E<i32>\n"], "D,E,G"),

        // SPEC 8.3.3: T.Item through a recursive Contract is no summary part, so the summaries stay finite.
        ["recursive Contract"] = (["contract Src\n    associate Item is Src\n", "struct X<T>\n    T is Src\n    let a: T\n    let b: Y<T.Item>\n",
            "struct Y<U>\n    U is Src\n    let c: X<U>\n"], "X,Y"),

        // A completed projection adds the edge to its binding's node: S's binding stores D.
        ["binding"] = ([Src, StoresItem, "struct S\n    Self is Src\n    associate Src.Item is E<D>\n", "struct D\n    let x: X<S>\n",
            "struct E<U>\n    let u: U\n"], "D"),

        // SPEC 8.8.2: a conditional binding is walked and proven in its conformance scope, where J is Src2; an inherited one on the base
        // clause's instance.
        ["conditional binding"] = ([Src, Src2, "struct K<J>\n" + Conditional, StoresItem, ValIsD, "struct D\n    let x: X<K<S2>>\n"], "D"),
        ["conditional binding finite"] = ([Src, Src2, "struct K<J>\n" + Conditional, StoresItem, "struct S3\n    Self is Src2\n    associate Src2.Val is i32\n",
            "struct D\n    let x: X<K<S3>>\n"], string.Empty),
        ["inherited binding"] = ([Src, Src2, "open struct KB<J>\n" + Conditional, "struct K<J> : KB<J>\n    let z: i32\n", StoresItem, ValIsD,
            "struct D\n    let x: X<K<S2>>\n"], "D"),
        ["inherited binding finite"] = ([Src, Src2, "open struct KB<J>\n" + Conditional, "struct K<J> : KB<J>\n    let z: i32\n", StoresItem,
            "struct S3\n    Self is Src2\n    associate Src2.Val is i32\n", "struct D\n    let x: X<K<S3>>\n"], string.Empty),
        ["two-step binding"] = ([Src, "contract Src3\n    associate Val is Src2\n", "contract Src2\n    associate Val2\n",
            "struct K<J>\n    Self is Src when J is Src3\n        associate Src.Item is J.Val.Val2\n", StoresItem,
            "struct S4\n    Self is Src3\n    associate Src3.Val is S5\n", "struct S5\n    Self is Src2\n    associate Src2.Val2 is D\n",
            "struct D\n    let x: X<K<S4>>\n"], "D"),

        // A projection of a pair target is a summary part (PLAN G327: W's Field is also reported UnprovenConstraint_Kd).
        ["pair target"] = ([Src, "struct W<s/T>\n    T is Src\n    let b: T.Item\n", "struct S\n    Self is Src\n    associate Src.Item is D\n",
            "struct D\n    let w: W<rc/S>\n"], "D"),
    };

    // Every order of up to four declarations, else each rotation of the declarations and of their reverse.
    private static IEnumerable<int[]> Orders(int count)
    {
        if (count <= 4)
        {
            return Permutations(Enumerable.Range(0, count).ToArray());
        }

        return Enumerable.Range(0, count).SelectMany(r => new[] { false, true }.Select(reverse =>
            Enumerable.Range(0, count).Select(i => reverse ? count - 1 - ((i + r) % count) : (i + r) % count).ToArray()));
    }

    private static IEnumerable<int[]> Permutations(int[] items)
        => items.Length <= 1 ? [items] : items.SelectMany((x, i) => Permutations([.. items.Where((_, j) => j != i)]).Select(rest => (int[])[x, .. rest]));
}
