// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class FrontEndSyntaxTest
{
    [Theory]
    [InlineData("abstract open struct S\n    let value: i32", "abstract")]
    [InlineData("virtual group G\n    func f() => ()", "virtual")]
    [InlineData("override rootgroup G\n    let value: i32", "override")]
    [InlineData("abstract enum E\n    A", "abstract")]
    [InlineData("abstract contract C\n    func f()", "abstract")]
    [InlineData("public abstract open struct S", "abstract")]
    [InlineData("virtual override abstract public unsafe func f() => ()", "abstract")]
    [InlineData("abstract specialize func f<i32>() => ()", "abstract")]
    [InlineData("struct S\n    virtual init() => ()", "virtual")]
    [InlineData("struct S\n    override public drop => ()", "override")]
    [InlineData("struct S\n    virtual public let value: i32", "virtual")]
    [InlineData("struct S\n    override var value: i32", "override")]
    [InlineData("struct S\n    abstract computed value: i32\n        get(self: ref/Self) -> i32 => 1", "abstract")]
    [InlineData("struct S\n    var value: i32\n        abstract get\n        set", "abstract")]
    [InlineData("struct S\n    var value: i32\n        get\n        override open set", "override")]
    [InlineData("struct S\n    computed value: i32\n        virtual get(self: ref/Self) -> i32\n            return 1", "virtual")]
    [InlineData("contract C\n    abstract property value: i32 has get", "abstract")]
    [InlineData("contract C\n    override associate Element", "override")]
    [InlineData("contract C\n    property value: i32 has abstract open get, set", "abstract")]
    [InlineData("contract C\n    property value: i32 has get, virtual set", "virtual")]
    [InlineData("contract C\n    property value: i32\n        abstract get(self: ref/Self) -> i32", "abstract")]
    [InlineData("#if true\nabstract struct S", "abstract")]
    public void UnsupportedOrMisplacedModifiersReportOneCauseAndRecover(string source, string modifier)
    {
        var tree = Parse(source + "\nstruct Following\n");
        var diagnostic = Assert.Single(TestDiagnostics.Of(tree));
        Assert.Equal(modifier == "abstract" ? "UnavailableFeature_Kd" : "MisplacedSyntax_Kd", diagnostic.Code);
        Assert.Equal(source.IndexOf(modifier, StringComparison.Ordinal), diagnostic.Span.Start);
        Assert.Equal(modifier.Length, diagnostic.Span.Length);
        Assert.Contains(modifier, diagnostic.Message);
        Assert.Contains(tree.RootKoto.NestedContainers, x => x.Name == "Following");
        VerifyParents(tree.RootKoto);
    }

    [Theory]
    [InlineData("struct abstract\n    let override: i32\n    func virtual(self: ref/Self) -> i32 => self.override")]
    [InlineData("func virtual(x: i32) -> i32 => x\nlet override: i32 = virtual(1)")]
    [InlineData("let value = x.abstract()\nvirtual(value)")]
    [InlineData("abstract\nlet next: i32 = 2")]
    [InlineData("func f()\n    abstract\n    let next: i32 = 2")]
    [InlineData("func f()\n    abstract\nstruct Next")]
    [InlineData("abstract /* comment */\nstruct Next")]
    [InlineData("abstract\r\nfunc next() => ()")]
    [InlineData("let abstract = func() => ()\nabstract()")]
    public void UnavailableModifierSpellingsRemainOrdinaryNames(string source)
        => AssertValid(Parse(source));

    [Theory]
    [InlineData("#if false\nabstract open struct S\n    let value: i32\nstruct Next")]
    public void ExcludedSyntaxReportsUnavailableModifiers(string source)
        => Assert.Equal("UnavailableFeature_Kd", Assert.Single(TestDiagnostics.Of(Parse(source))).Code);

    [Fact]
    public void UnavailableDeclarationRecoveryRetainsIndependentSiblings()
    {
        var tree = Parse("struct S\n    abstract func removed()\n        func nested() => ()\n    func retained() => ()\nstruct Following");
        Assert.Equal("UnavailableFeature_Kd", Assert.Single(TestDiagnostics.Of(tree)).Code);
        var structure = tree.RootKoto.NestedContainers.Single(x => x.Name == "S");
        Assert.Equal("retained", Assert.IsType<FunctionKoto>(Assert.Single(structure.Members)).Name);
        Assert.Contains(tree.RootKoto.NestedContainers, x => x.Name == "Following");
    }

    [Theory]
    [InlineData("let a: [4 of i32] = [1, 2, 3, 4]")]
    [InlineData("let a: [(N * 2 + 1) of ref/T during source] = values")]
    [InlineData("let a: [Sizes.width of [2 of _]] = values")]
    [InlineData("let a: [::width of i32] = values")]
    [InlineData("func f<length N, T>(a: [N of T]) -> [N of T] => a")]
    [InlineData("let a = f<4, i32>(values)")]
    [InlineData("let a = f<(N + 1), i32>(values)")]
    [InlineData("let a = ::Root.member")]
    [InlineData("let a = source@move")]
    [InlineData("let a = pair.0.1")]
    [InlineData("let a = pair.0.name")]
    [InlineData("let a = $abort(\"failed\")")]
    [InlineData("let _日本語 = 1")]
    [InlineData("let a: [4 of _] = values")]
    [InlineData("let a: List<\n    List<i32>\n> = values")]
    [InlineData("let a: List <\n    List <i32>\n> = values")]
    [InlineData("func f <\n    T\n>(! value: T) => value")]
    [InlineData("let a = f<\n    [4 of i32]\n>(values)")]
    [InlineData("func f<\n    length N,\n    T\n>(value: [N of T]) => ()")]
    [InlineData("let a = source\n    .first()\n    .second()")]
    [InlineData("let a = if ready\n    .Some(1)\nelse\n    .None")]
    [InlineData("group Empty\nstruct EmptyType\ncontract EmptyContract")]
    [InlineData("enum E\n    #if true\n    A\n    #switch\n        #case true\n            B\n        #case _\n            C")]
    [InlineData("let a = x is Type and ready")]
    [InlineData("let a = x is not Group.Type or fallback")]
    [InlineData("let a = .Some(1)")]
    [InlineData("func f()\n    .None")]
    [InlineData("let a = func(x) => x")]
    [InlineData("let a = func[](x: i32) -> i32 => x")]
    [InlineData("let a = func[source@ref, var count, value@uniq](x) => x")]
    [InlineData("let a = func[var source@ref, var count@uniq, var value@move](x) => x")]
    [InlineData("func f()\n    require valid else => return\n    work()")]
    [InlineData("func f()\n    require valid\n    else\n        return\n    work()")]
    [InlineData("enum Option<T>\n    None\n    Some(T)")]
    [InlineData("enum E {a}\n    A\n    B(ref/T during a)\n    func f() => ()")]
    [InlineData("let a = match value\n    .Some(let x) if x > 0 => x\n    .None => 0\n    _ => -1")]
    [InlineData("let a = match value\n    Option<i32>.Some(var x) => x\n    (let a, (var b, _)) => b\n    (1,) => 1\n    () => 0")]
    [InlineData("open struct Base\nstruct Derived : Base\n    init(value: i32) : base(value)\n        return\n    drop\n        return")]
    [InlineData("contract Sequence : Base, Other\n    associate Element\n    func next(self: ref/Self) -> Element\n    func use<T>(value: T)\n        T is Comparable\n    property size: i32 has get")]
    [InlineData("struct S\n    Self is Sequence\n    associate Sequence.Element is i32")]
    [InlineData("func f<T>(x: T)\n    T.Element is Comparable\n    return")]
    [InlineData("func f<F>(x: F)\n    F is Callable<ref, (i32) -> bool>\n    return")]
    [InlineData("specialize func f<i32>(x: i32) -> i32 => x")]
    [InlineData("protected internal struct S\n    private protected func f() => ()")]
    [InlineData("group G\n    func f(! other => self: i32) => ()")]
    [InlineData("struct S\n    func f(self: ref/Self, value => other: i32) => ()")]
    public void PreservesSpecifiedSyntax(string source)
    {
        var tree = ParseSuccess(source);
        VerifyParents(tree.RootKoto);
        var builder = default(IndentedStringBuilder);
        try
        {
            tree.RootKoto.UnparseAll(ref builder);
            var text = builder.ToString();
            var roundTrip = ParseSuccess(text);
            VerifyParents(roundTrip.RootKoto);
        }
        finally
        {
            builder.Dispose();
        }
    }

    // SPEC F.6.1 BaseClause := ":" InheritanceReference and F.6.2 ContractParentList := ":" InheritanceReference
    // ("," InheritanceReference)*, with InheritanceReference := NamedReference | ContainerPath BorrowOrigin and F.5 NamedReference :=
    // ContainerPath OriginBindingSet?: a base or a Contract parent written as any other Type is one grammar record over it, the same in
    // selected and excluded syntax (SPEC 19.4), and nothing of it is left to the header.
    [Theory]
    [InlineData("ref/N")]
    [InlineData("ref/::N")]
    [InlineData("owner/N")]
    [InlineData("(N)")]
    [InlineData("((N))")]
    [InlineData("N?")]
    [InlineData("(N, i32)")]
    [InlineData("()")]
    [InlineData("[2 of N]")]
    [InlineData("(N) -> i32")]
    [InlineData("N(a)")]
    public void ABaseOrParentOutsideAnInheritanceReferenceIsOneRecordInEverySelection(string reference)
    {
        foreach (var format in (string[])["struct D : R\n    var d: i32 = 0\n", "contract D : R\n    associate A\n", "contract D : M, R\n    associate A\n"])
        {
            foreach (var excluded in (bool[])[false, true])
            {
                var declaration = format.Replace("R", reference, StringComparison.Ordinal);
                var source = "open struct N\ncontract M\n" + (excluded ? "#if false\n    " + declaration.Replace("\n    ", "\n        ", StringComparison.Ordinal) : declaration);
                var diagnostic = Assert.Single(TestDiagnostics.Of(Parse(source)));
                Assert.Equal("ExpectedSyntax_Kd", diagnostic.Code);
                Assert.Contains("inheritance reference", diagnostic.Message, StringComparison.Ordinal);
                Assert.Equal(source.LastIndexOf(reference, StringComparison.Ordinal), diagnostic.Span.Start);
                Assert.Equal(reference.Length, diagnostic.Span.Length);
            }
        }
    }

    // The valid twins: a path, a root name, Type arguments, a binding set or a BorrowOrigin, and a PathQualifier with its PathSuffixes
    // (what the reference names, and the slot count a BorrowOrigin binds, are Binding's).
    [Theory]
    [InlineData("N")]
    [InlineData("::N")]
    [InlineData("Api.N")]
    [InlineData("N<i32>")]
    [InlineData("N{k}")]
    [InlineData("::Api.N<i32>{k}")]
    [InlineData("N during a")]
    [InlineData("::Api.N<i32> during a")]
    [InlineData("(S{k}).Item")]
    [InlineData("(S during a).(C).Item")]
    [InlineData("(i32, u8).(C).Item")]
    [InlineData("[2 of u8].Element")]
    [InlineData("N.(C).Item")]
    [InlineData("Self")]
    public void ABaseOrParentInsideAnInheritanceReferenceHasNoRecordInEverySelection(string reference)
    {
        foreach (var format in (string[])["struct D : R\n    var d: i32 = 0\n", "contract D : R\n    associate A\n", "contract D : M, R\n    associate A\n"])
        {
            var declaration = format.Replace("R", reference, StringComparison.Ordinal);
            ParseSuccess(declaration);
            ParseSuccess("#if false\n    " + declaration.Replace("\n    ", "\n        ", StringComparison.Ordinal));
        }
    }

    // SPEC 23.1.2: a written Type outside its position's grammar (a base or parent that is no InheritanceReference, F.6.1; a group or a
    // fixed array after "::", F.5; an adaptation target's bound Container qualifier or borrow-layer Origin, 13.5.1; the Type after a
    // move/copy prefix or a legacy borrow prefix) stays bound beside its syntax record, so a failure inside it that its repair leaves
    // keeps its own record: the unresolved Name, the repeated set name.
    [Theory]
    [InlineData("struct D : (Missing)", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("struct D : ref/Missing", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("struct D : Missing?", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("struct D : GB<Missing>?", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("struct D : (B0, Missing)", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("struct D {b}: (Base{b})", "1*DuplicateBinding_Kd 1*ExpectedSyntax_Kd")]
    [InlineData("func f(v: ::(Missing)) => ()", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("func f(v: ::[2 of Missing]) => ()", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("struct D : ::(Missing)", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")] // The base's own rule reads the recovered Type: no record.
    [InlineData("func f(x: i32) -> i32 => x@((Missing{k}).Item)", "1*MisplacedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("func f(x: i32, z: i32)\n    let y = x@(ref/Missing during z)", "1*MisplacedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("func f(x: i32, z: i32)\n    let y = x@(uniq/Missing during z)", "1*MisplacedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("func f(x: i32, z: i32)\n    let y = x@(ref/((Missing{k}).Item) during z)", "2*MisplacedSyntax_Kd 1*UnresolvedBinding_Kd")] // Each form of the chain.
    [InlineData("func f(v: move/Missing) => ()", "1*MisplacedSyntax_Kd 1*UnresolvedBinding_Kd")] // SPEC 13.5.1: move and copy are operations.
    [InlineData("func f(x: i32)\n    let y = x@copy/Missing", "1*MisplacedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("func f(a: i32, v: ref{a}/Missing) => ()", "1*LegacyBorrowOrigin_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("func f(v: Missing.1) => ()", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")] // F.5: the path before a failed member stays bound.
    [InlineData("func f(v: ref/Missing.1) => ()", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("func f(v: Missing<i32>.1) => ()", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("func f(v: GB<Missing.1>) => ()", "1*ExpectedSyntax_Kd 1*UnresolvedBinding_Kd")]
    [InlineData("func f(v: i32.1) => ()", "1*ExpectedSyntax_Kd")]
    [InlineData("func f(v: B0.1) => ()", "1*ExpectedSyntax_Kd")]
    public void AFailureInsideAWrittenTypeOutsideItsGrammarKeepsItsRecord(string declaration, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile("open struct B0\nopen struct GB<T>\nopen struct Base {a}\n    public let value: ref/i32 during a\n    public init(value: ref/i32 during a) => self.value = value\n" + declaration + "\n"));

    [Theory]
    [InlineData("let require = 1")]
    [InlineData("    let a = 1")]
    [InlineData("#if false\nlet e\u0301 = 1")]
    [InlineData("let a: _ = value")]
    [InlineData("let a: [4 of _]")]
    [InlineData("func f(value: [4 of _]) => value")]
    [InlineData("let a: List<[4 of _]> = values")]
    [InlineData("let a: ([4 of _], i32) = values")]
    [InlineData("let a: [2 of List<[4 of _]>] = values")]
    [InlineData("let a = f<[4 of _]>(values)")]
    [InlineData("struct S\n    func f(self: ref/Self, self: ref/Self) => ()")]
    [InlineData("struct S\n    func f(! other => self: ref/Self) => ()")]
    [InlineData("enum E\n    A\n    func f(self: E = E.A) => ()")]
    [InlineData("contract C\n    func f(self: ref/Self, self: ref/Self)")]
    [InlineData("group G\n    static func f() => ()")]
    [InlineData("func f()\n    static let x = 1")]
    [InlineData("struct S\n    static var x: i32")]
    [InlineData("open group G")]
    [InlineData("open enum E\n    A")]
    [InlineData("struct S\n    open func f() => ()")]
    [InlineData("for (a, b,) in pairs\n    ()")]
    [InlineData("for () in pairs\n    ()")]
    [InlineData("let a = match x\n    let _ => 1\n    _ => 0")]
    [InlineData("let a = match x\n    .Some(var _) => 1\n    _ => 0")]
    [InlineData("let a = $unknown(1)")]
    [InlineData("let a = $abort()")]
    [InlineData("let a = $abort(message: \"failed\")")]
    [InlineData("let a = $abort(\"failed\",)")]
    [InlineData("let defer = 1")]
    [InlineData("let Self = 1")]
    [InlineData("let init = 1")]
    [InlineData("let public = 1")]
    [InlineData("let a: [1.5 of T] = values")]
    [InlineData("let a: [N + 1 of T] = values")]
    [InlineData("let a: [4 T] = values")]
    [InlineData("struct S<length N>")]
    [InlineData("let a = func[x@T]() => x")]
    [InlineData("let a = func(x?: T) => x")]
    [InlineData("let a = match x\n    Name => 1")]
    [InlineData("let a = match x\n    1.5 => 1")]
    [InlineData("let a = match x\n    .A() => 1")]
    [InlineData("let a = match x\n    1 + 2 => 1")]
    [InlineData("contract C\n    func f(x: T = value)")]
    [InlineData("contract C\n    func f() => value")]
    [InlineData("func f()\n        return")]
    [InlineData("let a = value.true")]
    [InlineData("let a = value.init")]
    [InlineData("enum Empty")]
    [InlineData("extension Future")]
    [InlineData("func f<i32>() => ()")]
    [InlineData("func f<List<T>>() => ()")]
    [InlineData("let a = value as Type")]
    [InlineData("let a: List<\nT\n> = values")]
    [InlineData("let a = 0x")]
    [InlineData("let a = 0B")]
    [InlineData("let a = 0o___")]
    [InlineData("let a = 0x_ + 1")]
    public void RejectsInvalidSyntax(string source)
        => Assert.NotEmpty(TestDiagnostics.Of(Parse(source)));

    [Theory]
    [InlineData("#if false\nstatic func f() => ()\nlet a = 1")]
    [InlineData("#if false\nopen group G\nlet a = 1")]
    public void ExcludedDeclarationsReportModifierPlacement(string source)
        => Assert.NotEmpty(TestDiagnostics.Of(Parse(source)));

    [Fact]
    public void RetainsCaptureAcquisitionAndUnevaluatedLengths()
    {
        var tree = ParseSuccess("let f = func[var count, source@ref](x) => x\nlet a: [(N + 1) of i32] = values");
        var items = tree.GeneratedFunction!.Body!.Items;
        var function = Assert.IsType<FunctionKoto>(Assert.IsType<FieldKoto>(items[0]).InitializerKoto);
        Assert.True(function.IsAnonymous);
        Assert.Null(function.Captures![0].Operation);
        Assert.True(function.Captures[0].IsMutable);
        Assert.Equal("ref", function.Captures[1].Operation);
        var array = Assert.IsType<FixedArrayTypeKoto>(Assert.IsType<FieldKoto>(items[1]).TypeKoto);
        Assert.IsType<ParenthesizedKoto>(array.Length);
    }

    [Theory]
    [InlineData("1.000000059604644775390625000000000001")]
    [InlineData("1e999999")]
    [InlineData("1e-999999")]
    [InlineData("1__234.5_678e+1_2")]
    public void RetainsExactDecimalSpellingUntilFitting(string literal)
    {
        var tree = ParseSuccess("let value = " + literal);
        var number = Assert.IsType<NumberLiteralKoto>(Assert.IsType<FieldKoto>(Assert.Single(tree.GeneratedFunction!.Body!.Items)).InitializerKoto);
        Assert.False(number.IsInteger);
        Assert.Equal(literal, number.SourceSpelling.ToString());
        Assert.Equal(literal, number.ToString());
        var restored = Tinyhand.TinyhandSerializer.Deserialize<Kotonoha>(Tinyhand.TinyhandSerializer.Serialize(tree))!;
        restored.OnDeserialized(Compilation.CreateForTest());
        AssertValid(restored);
        Assert.Equal(literal, Assert.IsType<FieldKoto>(Assert.Single(restored.GeneratedFunction!.Body!.Items)).InitializerKoto!.ToString());
    }

    // SPEC 8.4: a Contract declares Type parameters, and a refining Contract names its base applied to its own; a function result is
    // a Type, so `place` is an ordinary Name there (SPEC F.6).
    [Fact]
    public void ParsesContractTypeParametersAndReferenceRequirements()
    {
        var tree = ParseSuccess(
            """
            contract Indexable<Key>
                associate Element
                func index(self, key: ref/Key) -> ref/Element during self
            contract UniqIndexable<Key>: Indexable<Key>
                func indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self
            func place(place: i32) -> place => place
            """);
        var contracts = KotoTree.Walk(tree.RootKoto).OfType<ContractKoto>().ToArray();
        Assert.Equal(2, contracts.Length);
        Assert.Equal("Key", Assert.Single(contracts[0].GenericParameterNodes).Identifier);
        Assert.IsType<TypeSemanticsKoto>(Assert.Single(contracts[0].Members.OfType<FunctionKoto>()).ReturnType);
        Assert.Equal("Key", Assert.Single(contracts[1].GenericParameterNodes).Identifier);
        Assert.Single(contracts[1].Bases);
        Assert.Equal(SemanticsKind.Uniq, Assert.IsType<TypeSemanticsKoto>(Assert.Single(contracts[1].Members.OfType<FunctionKoto>()).ReturnType).SemanticsKind);
        var place = KotoTree.Walk(tree.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "place");
        Assert.Equal("place", place.ReturnType!.ToString());
    }

    // SPEC F.6.2 ConditionalConformance := "Self" "is" ContractReference "when" ConformanceConditions, with ContractReference :=
    // ContainerReference := NamedReference (F.5: ContainerPath OriginBindingSet?) and ConformanceCondition := ConstraintSubject "is"
    // ConformanceRequirement (atoms joined by "and"; no "not", no "or", also 8.8.1): a head or condition outside it is one grammar
    // record at the part, the same in selected and excluded syntax (SPEC 19.4); the "when" decides the production (PLAN G873).
    [Theory]
    [InlineData("Self is (X) when U is Copy", "(X)", "contract reference")]
    [InlineData("Self is ((X)) when U is Copy", "((X))", "contract reference")]
    [InlineData("Self is ref/X when U is Copy", "ref/X", "contract reference")]
    [InlineData("Self is X? when U is Copy", "X?", "contract reference")]
    [InlineData("Self is X during a when U is Copy", "X during a", "contract reference")]
    [InlineData("Self is not X when U is Copy", "not X", "contract reference")]
    [InlineData("Self is X and Y when U is Copy", "X and Y", "contract reference")]
    [InlineData("Self is X or Y when U is Copy", "X or Y", "contract reference")]
    [InlineData("Self is (X, Y) when U is Copy", "(X, Y)", "contract reference")]
    [InlineData("Self is () when U is Copy", "()", "contract reference")]
    [InlineData("Self is [2 of X] when U is Copy", "[2 of X]", "contract reference")]
    [InlineData("Self is X(a) when U is Copy", "X(a)", "contract reference")]
    [InlineData("Self is (X) -> i32 when U is Copy", "(X) -> i32", "contract reference")]
    [InlineData("U is X when U is Copy", "U", "conditional conformance's subject")]
    [InlineData("Self is X when U is not Copy", "not Copy", "conformance requirement")]
    [InlineData("Self is X when U is Copy or X", "Copy or X", "conformance requirement")]
    public void AConditionalConformanceHeadOrConditionOutsideItsGrammarIsOneRecordInEverySelection(string item, string at, string phrase)
    {
        foreach (var excluded in (bool[])[false, true])
        {
            var source = "contract X\ncontract Y\nstruct T<U>\n    " + (excluded ? "#if false\n        " : string.Empty) + item + "\n    var value: U\n";
            var diagnostic = Assert.Single(TestDiagnostics.Of(Parse(source)));
            Assert.Equal("ExpectedSyntax_Kd", diagnostic.Code);
            Assert.Contains(phrase, diagnostic.Message, StringComparison.Ordinal);
            Assert.Equal(source.IndexOf(item, StringComparison.Ordinal) + item.IndexOf(at, StringComparison.Ordinal), diagnostic.Span.Start);
        }
    }

    // SPEC 23.1.2: each part of the head is judged on its own input, so two independent failures give two records; an enum body reads the
    // same production.
    [Theory]
    [InlineData("struct T<U>\n    U is X.1 when U is Copy\n    var value: U", 2)]
    [InlineData("struct T<U>\n    U is (X) when U is Copy\n    var value: U", 2)]
    [InlineData("struct T<U>\n    U is X when U is not Copy\n    var value: U", 2)]
    [InlineData("struct T<U>\n    Self is X.1 when U is Copy\n    var value: U", 1)]
    [InlineData("enum E<V>\n    Self is (X) when V is Copy\n    A", 1)]
    public void AConditionalConformanceKeepsEachIndependentRecord(string declaration, int count)
    {
        foreach (var excluded in (bool[])[false, true])
        {
            var source = "contract X\n" + (excluded ? "#if false\n    " + declaration.Replace("\n", "\n    ", StringComparison.Ordinal) : declaration) + "\n";
            var diagnostics = TestDiagnostics.Of(Parse(source));
            Assert.Equal(count, diagnostics.Length);
            Assert.All(diagnostics, static x => Assert.Equal("ExpectedSyntax_Kd", x.Code));
        }
    }

    // The valid twins: a ContractReference (a path, a root name, a binding set, a primitive or Self TypeName; F.3.2's Semantics,
    // SemanticsCategory and Callable words are Names there, no requirement), conditions joined by "and", a list, a block; and every
    // plain ConstraintClause, whose RequirementAtom is Binding's to judge.
    [Theory]
    [InlineData("Self is X when U is Copy")]
    [InlineData("Self is ::X when U is Copy")]
    [InlineData("Self is Api.X when U is Copy")]
    [InlineData("Self is X{a} when U is Copy")]
    [InlineData("Self is i32 when U is Copy")]
    [InlineData("Self is ref when U is Copy")]
    [InlineData("Self is value when U is Copy")]
    [InlineData("Self is Callable<(i32) -> i32> when U is Copy")]
    [InlineData("Self is X when U is Copy and X")]
    [InlineData("Self is X when U is (Copy and X)")]
    [InlineData("Self is X when U is Copy, U is X")]
    [InlineData("Self is X when U is Copy\n        public func f(self: ref/Self) -> i32 => 0")]
    [InlineData("Self is (X)")]
    [InlineData("Self is ref/X")]
    [InlineData("Self is X?")]
    [InlineData("Self is X during a")]
    [InlineData("Self is not X")]
    [InlineData("Self is X or Y")]
    public void AConditionalConformanceInsideItsGrammarHasNoRecordInEverySelection(string item)
    {
        ParseSuccess("contract X\ncontract Y\nstruct T<U>\n    " + item + "\n    var value: U\n");
        ParseSuccess("contract X\ncontract Y\nstruct T<U>\n    #if false\n        " + item.Replace("\n    ", "\n        ", StringComparison.Ordinal) + "\n    var value: U\n");
    }

    // SPEC F.6.2 ContractReference := NamedReference in every position that writes one: F.5's PathSuffix "." "(" ContractReference ")"
    // "." Name in a Type, a ConstraintSubject and a requirement, F.9.1's PostfixSuffix and BoundContainerExpression, and a
    // ContractSelector's group (an AssociatedTypeName, an EffectSpecification): one reader, one record over the reference.
    [Theory]
    [InlineData("ref/Y")]
    [InlineData("(Y)")]
    [InlineData("Y?")]
    [InlineData("Y during a")]
    [InlineData("[2 of Y]")]
    [InlineData("(Y) -> i32")]
    [InlineData("Y(a)")]
    public void AContractReferenceOutsideItsGrammarIsOneRecordInEveryPosition(string reference)
    {
        foreach (var format in ContractReferencePositions())
        {
            foreach (var excluded in (bool[])[false, true])
            {
                var declaration = format.Replace("R", reference, StringComparison.Ordinal);
                var source = excluded ? "#if false\n    " + declaration.Replace("\n", "\n    ", StringComparison.Ordinal) + "\n" : declaration + "\n";
                var diagnostic = Assert.Single(TestDiagnostics.Of(Parse(source)));
                Assert.Equal("ExpectedSyntax_Kd", diagnostic.Code);
                Assert.Contains("contract reference", diagnostic.Message, StringComparison.Ordinal);
                Assert.Equal(source.IndexOf("(" + reference + ")", StringComparison.Ordinal) + 1, diagnostic.Span.Start);
            }
        }
    }

    [Theory]
    [InlineData("Y")]
    [InlineData("::Y")]
    [InlineData("Y{a}")]
    [InlineData("Api.Y<i32>")]
    public void AContractReferenceInsideItsGrammarHasNoRecordInEveryPosition(string reference)
    {
        foreach (var format in ContractReferencePositions())
        {
            var declaration = format.Replace("R", reference, StringComparison.Ordinal);
            ParseSuccess(declaration + "\n");
            ParseSuccess("#if false\n    " + declaration.Replace("\n", "\n    ", StringComparison.Ordinal) + "\n");
        }
    }

    // SPEC F.6.2 ContractSelector := ContainerPath | "(" ContractReference ")", read by one reader at both heads and judged once complete:
    // a PathQualifier needs its PathSuffix (F.5 ContainerPath := PathQualifier PathSuffix+), and no other grouped Type is a PathQualifier,
    // so a further suffix after "(" ContractReference ")" "." Name is the enclosing construct's.
    [Theory]
    [InlineData("struct S\n    associate (Y).K.Item is i32", ".Item", "ExpectedSyntax_Kd")]
    [InlineData("struct S\n    associate [2 of u8].Item is i32", "[2 of u8]", "ExpectedSyntax_Kd")]
    [InlineData("struct S\n    associate (Y during a).Item is i32", "Y during a", "ExpectedSyntax_Kd")]
    [InlineData("struct S\n    associate (Y, K).Item is i32", "(Y, K)", "ExpectedSyntax_Kd")]
    [InlineData("struct S\n    associate ().Item is i32", "()", "ExpectedSyntax_Kd")]
    [InlineData("struct S\n    associate ((Y) -> i32).Item is i32", "(Y) -> i32", "ExpectedSyntax_Kd")]
    [InlineData("struct S\n    associate (i32, u8).Item is i32", "(i32, u8)", "ExpectedSyntax_Kd")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect (X).K.h confined", ".h", "ExpectedSyntax_Kd")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect (Y, K).h confined", "(Y, K)", "ExpectedSyntax_Kd")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect ().h confined", "()", "ExpectedSyntax_Kd")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect [2 of u8].h confined", "[2 of u8]", "ExpectedSyntax_Kd")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect (Y during a).h confined", "Y during a", "ExpectedSyntax_Kd")]
    public void AContractSelectorOutsideItsGrammarIsOneRecordInEverySelection(string declaration, string at, string code)
    {
        foreach (var excluded in (bool[])[false, true])
        {
            var source = excluded ? "#if false\n    " + declaration.Replace("\n", "\n    ", StringComparison.Ordinal) + "\n" : declaration + "\n";
            var diagnostic = Assert.Single(TestDiagnostics.Of(Parse(source)));
            Assert.Equal(code, diagnostic.Code);
            Assert.Equal(source.LastIndexOf(at, StringComparison.Ordinal), diagnostic.Span.Start);
        }
    }

    // The valid twins: "(" ContractReference ")" "." Name, a path from a Name, a root name or Type arguments, and a ContainerPath headed
    // by a PathQualifier with its PathSuffix, at both heads (an EffectSpecification's selector is no longer read by a Name loop of its own).
    [Theory]
    [InlineData("struct S\n    associate (Y{a}).Item is i32")]
    [InlineData("struct S\n    associate (Y).Item is i32")]
    [InlineData("struct S\n    associate Y.Item is i32")]
    [InlineData("struct S\n    associate (i32, u8).Y.Item is i32")]
    [InlineData("struct S\n    associate (Y{a}).K.Item is i32")]
    [InlineData("struct S\n    associate (Y during a).K.Item is i32")]
    [InlineData("struct S\n    associate ((i32) -> i32).K.Item is i32")]
    [InlineData("struct S\n    associate [2 of u8].K.Item is i32")]
    [InlineData("struct S\n    associate (i32, u8).(K).Item.Y.Item is i32")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect (Y{a}).h confined")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect (Y).h confined")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect X.h confined")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect ::X.h confined")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect Y<i32>.h confined")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect (i32, u8).X.h confined")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect [2 of u8].K.h confined")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect K.(Y).Item.h confined")]
    public void AContractSelectorInsideItsGrammarHasNoRecordInEverySelection(string declaration)
    {
        ParseSuccess(declaration + "\n");
        ParseSuccess("#if false\n    " + declaration.Replace("\n", "\n    ", StringComparison.Ordinal) + "\n");
    }

    // SPEC F.5 NamedType := NamedReference OriginApplication? (F.8: "never a call, Tuple or grouping"): the OriginApplication ends the
    // NamedType, so a PathSuffix, Type arguments or a second application after it is the enclosing construct's syntax error, in every
    // Type position, a selector head and a requirement (whose suffixes F.5's one loop reads), the same in selected and excluded syntax.
    // The counts: one record at the token after the application, except the named second records on these rejected programs (rule W,
    // counted): a Type argument's list closes at "." and the parameter list's own check meets the written ">" (G848); a result Type's
    // header skips the body (the missing-body record, D1's line); a base clause's record over Y(a) and the header's at ".Item"; the
    // associate head's "is" record and the line's end after the skipped requirement (G848's cascade).
    [Theory]
    [InlineData("func f(v: Y(a).Item) -> i32 => 0", ".Item", 1)]
    [InlineData("func f() -> i32\n    let x: Y(a).Item = 0\n    return 0", ".Item", 1)]
    [InlineData("func f(v: Option<Y(a).Item>) -> i32 => 0", ".Item", 2)]
    [InlineData("func f() -> Y(a).Item => g()", ".Item", 2)]
    [InlineData("func f<T>(v: T.Item(a).Z) -> i32 => 0", ".Z", 1)]
    [InlineData("func f(v: Y(a).(K).Item) -> i32 => 0", ".(K)", 1)]
    [InlineData("func f(v: Y(a)<i32>) -> i32 => 0", "<i32>", 1)]
    [InlineData("func f(v: Missing(a).Item) -> i32 => 0", ".Item", 1)]
    [InlineData("struct A\n    associate Y(a).Item is i32", ".Item", 2)]
    [InlineData("func f<T>(v: T) -> i32\n    T is Y(a).Item\n    return 0", ".Item", 1)]
    [InlineData("func f<T>(v: T) -> i32\n    T is K.Item(a).Z\n    return 0", ".Z", 1)]
    [InlineData("func f<T>(v: T) -> i32\n    T is Y(a)(b)\n    return 0", "(b)", 1)]
    [InlineData("func f<T>(v: T) -> i32\n    T is Y(a)<i32>\n    return 0", "<i32>", 1)]
    [InlineData("struct T<V>\n    Self is Y(a).Item when V is Copy", ".Item", 1)]
    [InlineData("open struct D: Y(a).Item", ".Item", 2)]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect K(a).h confined", "K(a)", 1)]
    public void AnOriginApplicationEndsANamedTypeInEverySelection(string declaration, string at, int count)
    {
        foreach (var excluded in (bool[])[false, true])
        {
            var source = excluded ? "#if false\n    " + declaration.Replace("\n", "\n    ", StringComparison.Ordinal) + "\n" : declaration + "\n";
            var diagnostics = TestDiagnostics.Of(Parse(source));
            Assert.Equal(count, diagnostics.Length);
            Assert.Contains(diagnostics, x => x.Span.Start == source.LastIndexOf(at, StringComparison.Ordinal));
            Assert.All(diagnostics, static x => Assert.Contains(x.Code, (string[])["ExpectedSyntax_Kd", "MissingSyntax_Kd"]));
        }
    }

    // The valid twins: an application closing an associated-Type name (in a Type, a subject, a requirement, a declaration), the Type
    // continuing around it (an argument, '?', a Semantics prefix), and the requirement atoms F.5's loop now reads after the head word.
    [Theory]
    [InlineData("func f<T>(v: T.Item(a)) -> i32 => 0")]
    [InlineData("func f<T>(v: Option<T.Item(a)>) -> i32 => 0")]
    [InlineData("func f<T>(v: T.Item(a)?) -> i32 => 0")]
    [InlineData("func f<T>(v: ref/T.Item(a)) -> i32 => 0")]
    [InlineData("func f(v: Y(a)) -> i32 => 0")]
    [InlineData("func f(v: S{a}) -> i32 => 0")]
    [InlineData("contract D\n    associate Item(a) is K")]
    [InlineData("func f<U>(v: U) -> i32\n    U.Item(a) is K\n    return 0")]
    [InlineData("func f<T, U>(v: T) -> i32\n    T is U.Item(a)\n    return 0")]
    [InlineData("func f<T, U>(v: T) -> i32\n    T is U.(K).Item(a)\n    return 0")]
    [InlineData("func f<T, U>(v: T) -> i32\n    T is U.Item(a)?\n    return 0")]
    [InlineData("func f<T>(v: T) -> i32\n    T is Y(a)\n    return 0")]
    [InlineData("func f<T>(v: T) -> i32\n    T is Array<i32>\n    return 0")]
    [InlineData("func f<T>(v: T) -> i32\n    T is Option<i32>?\n    return 0")]
    [InlineData("func f<T>(v: T) -> i32\n    T is G<i32>.Item\n    return 0")]
    [InlineData("func f<T>(v: T) -> i32\n    T is Callable<(i32) -> i32>\n    return 0")]
    [InlineData("func f<T>(v: T) -> i32\n    T is value\n    return 0")]
    [InlineData("func f<T>(v: T) -> i32\n    T is ref\n    return 0")]
    [InlineData("func f<T>(v: T) -> i32\n    T is K and Y\n    return 0")]
    [InlineData("func f<T>(v: T) -> i32\n    T is not K\n    return 0")]
    public void AnOriginApplicationClosingANamedTypeHasNoRecordInEverySelection(string declaration)
    {
        ParseSuccess(declaration + "\n");
        ParseSuccess("#if false\n    " + declaration.Replace("\n", "\n    ", StringComparison.Ordinal) + "\n");
    }

    // SPEC F.5 PathSuffix := "." TypeSegment | "." "(" ContractReference ")" "." Name (F.9.1's PostfixSuffix the same): a grouped Contract
    // after "." is one suffix with its ". Name", in every reader of it (a Type, a subject, a requirement, a selector head, a runtime type
    // test's target, an expression's PostfixSuffix and BoundContainerExpression): a lone grouped Contract is one record at the group; a
    // selector split before the PathSuffix's own Name is none.
    [Theory]
    [InlineData("func f(v: K.(Y)) -> i32 => 0", "(Y)", "grouped Contract followed by its member Name")]
    [InlineData("func f<U>(u: U) -> i32\n    U.(K) is Copy\n    return 0", "(K)", "grouped Contract followed by its member Name")]
    [InlineData("func f<T, U>(v: T) -> i32\n    T is U.(K)\n    return 0", "(K)", "grouped Contract followed by its member Name")]
    [InlineData("func f(x: i32) -> bool => x is K.(Y)", "(Y)", "grouped Contract followed by its member Name")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect X.(Y) confined", "(Y)", "grouped Contract followed by its member Name")]
    [InlineData("func f(v: Api.N) -> i32\n    let r = v.(Y)\n    return 0", "(Y)", "grouped Contract followed by its member Name")]
    [InlineData("func f(z: i32) -> i32\n    let r = (Api.N{z}).(Y)\n    return 0", "(Y)", "grouped Contract followed by its member Name")]
    [InlineData("struct A\n    associate X.(Y).Item is i32", "X.(Y)", "contract reference")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect X.(Y).h confined", "X.(Y)", "contract reference")]
    public void AGroupedContractWithoutItsMemberNameIsOneRecordInEverySelection(string declaration, string at, string phrase)
    {
        foreach (var excluded in (bool[])[false, true])
        {
            var source = excluded ? "#if false\n    " + declaration.Replace("\n", "\n    ", StringComparison.Ordinal) + "\n" : declaration + "\n";
            var diagnostic = Assert.Single(TestDiagnostics.Of(Parse(source)));
            Assert.Equal("ExpectedSyntax_Kd", diagnostic.Code);
            Assert.Contains(phrase, diagnostic.Message, StringComparison.Ordinal);
            Assert.Equal(source.IndexOf(at, StringComparison.Ordinal), diagnostic.Span.Start);
        }
    }

    [Theory]
    [InlineData("func f<T>(v: T.(K).Item) -> i32 => 0")]
    [InlineData("func f<U>(u: U) -> i32\n    U.(K).Item is Copy\n    return 0")]
    [InlineData("func f<T, U>(v: T) -> i32\n    T is U.(K).Item\n    return 0")]
    [InlineData("struct A\n    associate X.(Y).Item.Z is i32")]
    [InlineData("contract E\n    func h(self: ref/Self)\n    effect K.(Y).Item.h confined")]
    [InlineData("func f(v: Api.N) -> i32\n    let r = v.(Y).m\n    return 0")]
    [InlineData("func f(z: i32) -> i32\n    let r = (Api.N{z}).(Y).m\n    return 0")]
    public void AGroupedContractWithItsMemberNameHasNoRecordInEverySelection(string declaration)
    {
        ParseSuccess(declaration + "\n");
        ParseSuccess("#if false\n    " + declaration.Replace("\n", "\n    ", StringComparison.Ordinal) + "\n");
    }

    // The positions of F.6.2's ContractReference, R the reference.
    private static string[] ContractReferencePositions() =>
    [
        "func f(v: Api.N.(R).Item) -> i32 => 0",
        "func f<U>(u: U) -> i32\n    U.(R).Item is K\n    return 0",
        "func f<U>(u: U) -> i32\n    U is Api.N.(R).Item\n    return 0",
        "func f(v: Api.N) -> i32\n    let r = v.(R).m\n    return 0",
        "func f(z: i32) -> i32\n    let r = (Api.N{z}).(R).m\n    return 0",
        "struct S\n    associate (R).Item is i32",
        "contract E\n    func h(self: ref/Self)\n    effect (R).h confined",
    ];
}
