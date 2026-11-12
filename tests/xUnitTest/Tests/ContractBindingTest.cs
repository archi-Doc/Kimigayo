// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ContractBindingTest
{
    private const string Projected = "Self is Iter<S.(HasItem).Item>\n    associate Tag is bool";
    private const string Uses = "func f(a: ref/B<SrcI>, b: ref/B<Src>) -> ()\n    use(a)\n    use(b)";
    private const string NameKey = "struct Name\n    Self is Copy\n    public let id: isize\n    public init(id: isize) => self.id = id\n";

    private const string Added = "S.(HasItem).Item is PrimitiveInteger\n    Self is Addable<S.(HasItem).Item>\n    associate Output is i32\n" +
        "    public func added(self: ref/Self, right: ref/S.(HasItem).Item) -> i32 => 6";

    private const string Add = "func add<T, U>(a: T, b: U.(HasItem).Item) -> T.Output\n    U is HasItem\n    T is Addable<U.(HasItem).Item>\n" +
        "    return a + b\n";

    private const string Keyed = "Self is Indexable<Option<S.(HasItem).Item>>\n    Self is Indexable<Name>\n" +
        "    associate Indexable<Option<S.(HasItem).Item>>.Element is i32\n    associate Indexable<Name>.Element is i64\n    var small: i32 = 3\n" +
        "    var large: i64 = 4\n    public func index(self, key: ref/Option<S.(HasItem).Item>) -> ref/i32 during self => self.small@ref\n" +
        "    public func index(self, key: ref/Name) -> ref/i64 during self => self.large@ref";

    private const string Get32 = "\n    public func get(self: ref/Self) -> i32 => 0";
    private const string Get64 = "\n    public func get(self: ref/Self) -> i64 => 0";

    private const string NeedOf = "func need<T>(v: T) -> T\n    T is ";
    private const string UseOfC = "\n    return v@move\nfunc use<T>(v: T) -> T\n    T is C\n    return need(v@move)";

    private const string PutOfT = "contract Put<T>\n    func put<U>(self: uniq/Self, u: U)\n        U is T\n";
    private const string PutAtI32 = PutOfT + "struct S\n    Self is Put<i32>\n    public func put<U>(self: uniq/Self, u: ";
    private const string PutAtE = PutOfT + "struct Box<E>\n    Self is Put<E>\n    public func put<U>(self: uniq/Self, u: U)\n        U is ";
    private const string PutUW = "contract Put\n    func put<U, W>(self: uniq/Self, u: U, w: W)\n        U is W\nstruct S\n    Self is Put\n    public func put<U, W>(self: uniq/Self, u: U, w: W)";
    private const string GetU = "contract Get<T>\n    func get<U>(self: ref/Self, u: U) -> U\n        U is T\nstruct S\n    Self is Get<i32>\n    public func get<U>(self: ref/Self, u: U) -> ";
    private const string PutSelf = "contract Put\n    func put<U>(self: uniq/Self, u: U)\n        U is Self\nstruct S\n    Self is Put\n    public func put<U>(self: uniq/Self, u: U)\n        U is ";
    private const string ViewIsL = "struct View {source}\n    Self is L\n    let data: ref/i32 during source\n";

    [Fact]
    public void AppendingSourceRevokesRetainedConformanceAndPropertyCertificates()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    property item: i32 has get\nstruct S\n    Self is C\n    public let item: i32");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var type = Container(c, "S");
        var contract = Container(c, "C").SymbolOf()!;
        var conformance = Assert.IsType<BoundConformance>(c.Binding.GetConformance(type.TypeOf()!, contract));
        var property = Assert.IsType<PropertyKoto>(Assert.Single(type.Members)).SymbolOf()!.Property!;
        Assert.True(conformance.IsVerified);
        Assert.True(property.IsVerified);

        c.Kotonoha.AddSource(new SourceDocument("Added.kimi", "func added() => ()"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(conformance.IsVerified);
        Assert.False(property.IsVerified);
        Assert.Null(c.Binding.GetConformance(type.TypeOf()!, contract));
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Same(conformance, c.Binding.GetConformance(type.TypeOf()!, contract));
        Assert.True(conformance.IsVerified);
        Assert.True(property.IsVerified);
    }

    [Fact]
    public void VerifiedMappingUsesDeclarationIdentities()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func read(self: ref/Self) -> i32\nstruct S\n    Self is C\n    public func read(self: ref/Self) -> i32 => 1");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var contract = Container(c, "C").SymbolOf()!;
        var type = Container(c, "S").TypeOf()!;
        var mapping = Assert.IsType<BoundConformance>(c.Binding.GetConformance(type, contract));
        var witness = Assert.Single(mapping.Witnesses);
        Assert.Equal(Assert.Single(contract.Contract!.Requirements), witness.Identity);
        Assert.Equal("read", witness.Implementation.Name);
        Assert.NotSame(witness.Requirement, witness.Implementation);
    }

    [Theory]
    [InlineData("public func read(self: ref/Self) -> i32 => 1", true)]
    [InlineData("public func read(self: ref/Self) -> string => \"x\"", false)]
    [InlineData("public func read(self: ref/Self during static) -> i32 => 1", false)]
    [InlineData("private func read(self: ref/Self) -> i32 => 1", false)]
    [InlineData("public unsafe func read(self: ref/Self) -> i32 => 1", false)]
    [InlineData("public func read() -> i32 => 1", false)]
    [InlineData("public func other(self: ref/Self) -> i32 => 1", false)]
    public void ImplementationContractIsCheckedAfterIdentification(string member, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    func read(self: ref/Self) -> i32\nstruct S\n    Self is C\n    {member}");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("    U is Copy\n", true)]
    [InlineData("    U is Owned\n", false)]
    public void RequirementPremisesMustProveImplementationConstraints(string premise, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    func apply<T>(value: T)\n        T is Copy\nstruct S\n    Self is C\n    public func apply<U>(value: U)\n{premise.Replace("    ", "        ")}        ()");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void DiamondRefinementRetainsOneRequirementIdentity()
    {
        var c = CompilationTestHelper.ParseSuccess("contract A\n    func f()\ncontract B: A\ncontract C: A\ncontract D: B, C\nstruct S\n    Self is D\n    Self is A\n    public func f() => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var d = Container(c, "D").SymbolOf()!.Contract!;
        Assert.Single(d.Requirements);
        Assert.Equal(3, d.Ancestors.Count);
        Assert.Equal(Container(c, "A").SymbolOf()!.Contract!.Requirements[0], d.Requirements[0]);
    }

    [Theory]
    [InlineData("contract A: B\ncontract B: A")]
    [InlineData("contract A: A")]
    [InlineData("struct S\ncontract A: S")]
    [InlineData("contract A\n    Self is A")]
    public void InvalidRefinementAndContractHeadersFail(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.False(c.Bind().IsComplete);
    }

    // SPEC 8.5.1 Qualification, 8.5.3: in a Contract, `associate C.X is R` names an ancestor C, with its Type arguments, and one of
    // its associated Types; the Contract itself, under any path, is no ancestor.
    [Theory]
    [InlineData("contract D\n    associate Item\n    associate D.Item is i32", false)]
    [InlineData("group G\n    contract D\n        associate Item\n        associate G.D.Item is i32", false)]
    [InlineData("contract D<T>\n    associate Item\n    associate D<T>.Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract D\n    associate C.Item is i32", false)]
    [InlineData("contract C<T>\n    associate Item\ncontract D: C<i32>\n    associate C<i64>.Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\n    associate C.Item is i32", true)]
    [InlineData("contract C<T>\n    associate Item\ncontract D: C<i32>\n    associate C<i32>.Item is i32", true)]
    [InlineData("contract C<T>\n    associate Item\ncontract D<T>: C<T>\n    associate C<T>.Item is T\nfunc f<U>(x: U.(C<i32>).Item) -> i32\n    U is D<i32>\n    return x", true)]
    [InlineData("contract C\n    associate Item\ncontract D: C\ncontract E: D\n    associate (C).Item is i32", true)]
    [InlineData("contract C\n    associate Item\ncontract D: C\n    associate ::C.Item is i32", true)]
    [InlineData("contract C<T>\n    associate Item\ncontract D: C<i32>\n    associate (C<i32>).Item is i32", true)]
    [InlineData("contract C<T>\n    associate Item\ncontract D<U>: C<U>\n    associate (C<U>).Item is U", true)]
    [InlineData("contract L\n    associate View(a)\ncontract D: L\n    associate (L).View(a) is ref/i32 during a", true)]
    [InlineData("group G\n    public contract C\n        associate Item\ncontract D: G.C\n    associate (G.C).Item is i32", true)]
    [InlineData("contract C\n    associate Item\ngroup G\n    group C\n        func z() -> i32 => 0\n    contract D: ::C\n        associate (C).Item is i32", true)]
    [InlineData("contract C\n    associate Item\ncontract K\n    associate Item\ncontract D: C, K\n    associate (C).Item is i32\n    associate (K).Item is i64", true)]
    [InlineData("contract C\n    associate Item\ncontract D\n    associate (C).Item is i32", false)]
    [InlineData("contract C<T>\n    associate Item\ncontract D: C<i32>\n    associate (C<i64>).Item is i32", false)]
    [InlineData("contract C<U>\n    associate Item\ncontract D<U>: C<U>\n    associate (C<i32>).Item is i32", false)]
    [InlineData("contract D<U>\n    associate Item\n    associate (D<i32>).Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\n    associate (C).Missing is i32", false)]
    [InlineData("contract D\n    associate Item\n    associate Self.Item is i32", false)]
    [InlineData("contract D\n    associate Item\n    associate ::D.Item is i32", false)]
    [InlineData("contract D\n    associate Item\n    associate (D).Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\n    associate ((C)).Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\n    associate (Self).Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\ncontract E: C\n    associate D.Item is i32", false)]
    [InlineData("group G\n    contract C\n        associate Item\n    contract D: C\n        associate (G.D).Item is i32", false)]
    public void AContractsAssociatedSpecificationNamesAnAncestor(string source, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssociatedType_Kd);
        }
    }

    // SPEC 8.5.1 Qualification (G683): in a struct, an enum or a conditional block, a selector, grouped, rooted or a path, names a direct
    // conformance or an ancestor of one, with its Type arguments; a descendant, the Type itself or a group in a group names none.
    [Theory]
    [InlineData("contract C\n    associate Item\nstruct S\n    Self is C\n    associate (C).Item is i32", true)]
    [InlineData("contract C<T>\n    associate Item\nstruct S\n    Self is C<i32>\n    associate (C<i32>).Item is i32", true)]
    [InlineData("contract C<T>\n    associate Item\nstruct S<U>\n    Self is C<U>\n    associate (C<U>).Item is U", true)]
    [InlineData("contract C<T>\n    associate Item\nstruct S\n    Self is C<i32>\n    Self is C<i64>\n    associate (C<i32>).Item is i32\n    associate (C<i64>).Item is i64", true)]
    [InlineData("contract C\n    associate Item\ncontract D: C\nstruct S\n    Self is D\n    associate (C).Item is i32", true)]
    [InlineData("contract C\n    associate Item\nstruct S\n    Self is C\n    associate ::C.Item is i32", true)]
    [InlineData("contract C<T>\n    associate Item\nstruct S\n    Self is C<i32>\n    associate ::C<i32>.Item is i32", true)]
    [InlineData("struct S\n    contract Inner\n        associate Item\n    Self is Inner\n    associate Self.Inner.Item is i32", true)]
    [InlineData("contract C\n    associate Item\ngroup G\n    group C\n        func z() -> i32 => 0\n    struct S\n        Self is ::C\n        associate (C).Item is i32", true)]
    [InlineData("contract C\n    associate Item\nenum E\n    A\n    Self is C\n    associate (C).Item is i32", true)]
    [InlineData("contract C\n    associate Item\ncontract D: C\nenum E\n    A\n    Self is D\n    associate ::C.Item is i32", true)]
    [InlineData("contract C\n    associate Item\nstruct W<T>\n    Self is C when T is Copy\n        associate (C).Item is T", true)]
    [InlineData("contract C\n    associate Item\ncontract D: C\nstruct W<T>\n    Self is D when T is Copy\n        associate (C).Item is T", true)]
    [InlineData("contract C<T>\n    associate Item\nstruct S\n    Self is C<i32>\n    associate (C<i64>).Item is i32", false)]
    [InlineData("contract C<U>\n    associate Item\nstruct S<T>\n    Self is C<T>\n    associate (C<i32>).Item is i32", false)]
    [InlineData("contract C\n    associate Item\nstruct S\n    Self is C\n    associate (Self).Item is i32", false)]
    [InlineData("contract C\n    associate Item\nstruct S\n    Self is C\n    associate ((C)).Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\nstruct S\n    Self is C\n    associate (D).Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\nstruct S\n    Self is C\n    associate D.Item is i32", false)]
    [InlineData("struct C\n    let v: i32 = 0\ncontract D\n    associate Item\nstruct S\n    Self is D\n    associate (C).Item is i32", false)]
    [InlineData("contract C\n    associate Item\nenum E\n    A\n    associate (C).Item is i32", false)]
    [InlineData("contract C\n    associate Item\nenum E\n    A\n    Self is C\n    associate Self.Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\nenum E\n    A\n    Self is C\n    associate (D).Item is i32", false)]
    [InlineData("contract C\n    associate Item\nenum E\n    A\n    Self is C\n    associate ((C)).Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract K\n    associate Item\nstruct W<T>\n    Self is C when T is Copy\n        associate (K).Item is T", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\nstruct W<T>\n    Self is C when T is Copy\n        associate (D).Item is T", false)]
    [InlineData("contract C\n    associate Item\nstruct W<T>\n    Self is C when T is Copy\n        associate ((C)).Item is T", false)]
    public void ATypesAssociatedSpecificationNamesAConformance(string source, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssociatedType_Kd);
        }
    }

    // SPEC 8.5.3, 8.4.3 (G683): a Contract's specification, a member of the Contract and no Constraint clause, is still a premise of the
    // Contract: its conformers and descendants are held to it, and `T is D` supplies it.
    [Theory]
    [InlineData("struct S\n    Self is D\n    associate C.Item is i32" + Get32, true)]
    [InlineData("struct S\n    Self is D\n    associate C.Item is i64" + Get64, false)]
    [InlineData("struct S\n    Self is D" + Get32, true)]
    [InlineData("struct S\n    Self is D" + Get64, false)]
    [InlineData("contract E: D\nstruct S\n    Self is E\n    associate C.Item is i32" + Get32, true)]
    [InlineData("contract E: D\nstruct S\n    Self is E\n    associate C.Item is i64" + Get64, false)]
    [InlineData("contract E: D\n    associate (C).Item is i64\nstruct S\n    Self is E\n    associate C.Item is i32" + Get32, false)]
    [InlineData("struct W<T>\n    Self is D when T is Copy\n        associate C.Item is i32\n        public func get(self: ref/Self) -> i32 => 0", true)]
    [InlineData("struct W<T>\n    Self is D when T is Copy\n        associate C.Item is i64\n        public func get(self: ref/Self) -> i64 => 0", false)]
    [InlineData("func f<T>(x: ref/T) -> i32\n    T is D\n    return x.get()", true)]
    [InlineData("contract E: C\nfunc f<T>(x: ref/T) -> i32\n    T is E\n    return x.get()", false)]
    public void AContractsAssociatedSpecificationIsAPremise(string use, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate Item\n    func get(self: ref/Self) -> Self.Item\ncontract D: C\n    associate (C).Item is i32\n" + use);
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    // SPEC 8.5.1, 8.5.3 (G683): a premise through a generic Contract's selector, a Copy requirement and a declaration's own requirement.
    [Theory]
    [InlineData("contract C<U>\n    associate Item\ncontract D<U>: C<U>\n    associate (C<U>).Item is U\nstruct S\n    Self is D<i32>\n    associate C<i32>.Item is i32", true)]
    [InlineData("contract C<U>\n    associate Item\ncontract D<U>: C<U>\n    associate (C<U>).Item is U\nstruct S\n    Self is D<i32>\n    associate C<i32>.Item is i64", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\n    associate (C).Item is Copy\nstruct S\n    Self is D\n    associate C.Item is i32", true)]
    [InlineData("contract C\n    associate Item\ncontract D: C\n    associate (Missing).Item is i32\nstruct S\n    Self is D\n    associate C.Item is i32", false)]
    [InlineData("contract D\n    associate Item is Copy\nstruct S\n    Self is D\n    associate D.Item is i32", true)]
    public void AContractsAssociatedSpecificationRequirementHolds(string source, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    // SPEC F.5, 8.5.2 (G683, WrittenReference): a projection's `.(C).` holds one group around a NamedReference; `((C))` names no Contract
    // in a signature, a function's Constraint region or a Contract's Constraint.
    [Theory]
    [InlineData("func one<T>(source: ref/T) -> T.(C).Item\n    T is C\n    return source.get()", true)]
    [InlineData("func one<T>(source: ref/T) -> T.((C)).Item\n    T is C\n    return source.get()", false)]
    [InlineData("func one<T>(source: ref/T) -> T.(::C).Item\n    T is C\n    return source.get()", true)]
    [InlineData("func f<T>(x: ref/T) -> i32\n    T is C\n    T.(C).Item is i32\n    return 0", true)]
    [InlineData("func f<T>(x: ref/T) -> i32\n    T is C\n    T.((C)).Item is i32\n    return 0", false)]
    [InlineData("contract D: C\n    Self.(C).Item is i32", true)]
    [InlineData("contract D: C\n    Self.((C)).Item is i32", false)]
    public void AProjectionsContractIsOneGroupedReference(string use, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate Item\n    func get(self: ref/Self) -> Self.Item\n" + use);
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    // SPEC 8.5.1 Qualification, 8.5.2, 9.7 (PLAN G705): a Contract qualifier is a bound Contract reference, so a Contract with Type
    // parameters is named with its Type arguments in every associated selector and projection; without them it names no reference,
    // whether one bound reference of it is available or several.
    [Theory]
    [InlineData("contract D: C<i32>\n    associate C.Item is i32", false)]
    [InlineData("contract D: C<i32>\n    associate C<i32>.Item is i32", true)]
    [InlineData("    func f(self: ref/Self) -> Self.(C).Item", false)]
    [InlineData("    func f(self: ref/Self) -> Self.(C<T>).Item", true)]
    [InlineData("contract A: C<i32>\ncontract B: C<i64>\ncontract D: A, B\n    associate C.Item is i32", false)]
    [InlineData("contract A: C<i32>\ncontract B: C<i64>\ncontract D: A, B\n    associate C<i32>.Item is i32\n    associate C<i64>.Item is i64", true)]
    [InlineData("func f<U>(x: U.(::C<i32>).Item) -> i32\n    U is C<i32>\n    U.(C<i32>).Item is i32\n    return x", true)]
    [InlineData("contract D<T>: C<T>\n    associate C.Item is T", false)]
    [InlineData("contract B: C<i32>\ncontract D: B\n    associate C.Item is i32", false)]
    [InlineData("contract B: C<i32>\ncontract D: B\n    associate C<i32>.Item is i32", true)]
    [InlineData("contract D: C<i32>\n    Self.(C).Item is i32", false)]
    [InlineData("contract D: C<i32>\n    Self.(C<i32>).Item is i32", true)]
    [InlineData("struct S\n    Self is C<i32>\n    associate C.Item is i32", false)]
    [InlineData("struct S\n    Self is C<i32>\n    associate C<i32>.Item is i32", true)]
    [InlineData("contract B: C<i32>\nstruct S\n    Self is B\n    associate C.Item is i32", false)]
    [InlineData("contract B: C<i32>\nstruct S\n    Self is B\n    associate C<i32>.Item is i32", true)]
    [InlineData("struct S<U>\n    Self is C<i32> when U is Copy\n        associate C.Item is i32", false)]
    [InlineData("struct S<U>\n    Self is C<i32> when U is Copy\n        associate C<i32>.Item is i32", true)]
    [InlineData("func f<U>(x: U.(C).Item) -> i32\n    U is C<i32>\n    U.(C<i32>).Item is i32\n    return x", false)]
    [InlineData("func f<U>(x: U.(C<i32>).Item) -> i32\n    U is C<i32>\n    U.(C).Item is i32\n    return x", false)]
    [InlineData("func f<U>(x: U.(C<i32>).Item) -> i32\n    U is C<i32>\n    U.(C<i32>).Item is i32\n    return x", true)]
    [InlineData("struct S\n    Self is C<i32>\n    associate C<i32>.Item is i32\nfunc g(x: S.(C).Item) -> i32\n    return x", false)]
    [InlineData("struct S\n    Self is C<i32>\n    associate C<i32>.Item is i32\nfunc g(x: S.(C<i32>).Item) -> i32\n    return x", true)]
    [InlineData("func f<U>(x: i32) -> i64\n    U is C<i32>\n    U.(C<i32>).Item is i64\n    return x@(U.(C).Item)", false)]
    [InlineData("func f<U>(x: i32) -> i64\n    U is C<i32>\n    U.(C<i32>).Item is i64\n    return x@(U.(C<i32>).Item)", true)]
    [InlineData("func apply<U, F>(f: ref/F, x: i64) -> i32\n    U is C<i32>\n    U.(C<i32>).Item is i64\n    F is Callable<(U.(C).Item) -> i32>\n    return f(x)", false)]
    [InlineData("func apply<U, F>(f: ref/F, x: i64) -> i32\n    U is C<i32>\n    U.(C<i32>).Item is i64\n    F is Callable<(U.(C<i32>).Item) -> i32>\n    return f(x)", true)]
    [InlineData("struct W<U>\n    U is C<i32>\n    public let e: U.(C).Item", false)]
    [InlineData("struct W<U>\n    U is C<i32>\n    public let e: U.(C<i32>).Item", true)]
    [InlineData("    associate View(a)\ncontract D: C<i32>\n    associate C<i32>.View(a) is i32\nfunc f<U>(x: U.(C).View(static)) -> i32\n    U is D\n    return x", false)]
    [InlineData("    associate View(a)\ncontract D: C<i32>\n    associate C<i32>.View(a) is i32\nfunc f<U>(x: U.(C<i32>).View(static)) -> i32\n    U is D\n    return x", true)]
    public void AGenericContractQualifierTakesItsTypeArguments(string use, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess("contract C<T>\n    associate Item\n" + use);
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd && x.Node.ToString() == "C");
            Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
        }
    }

    // SPEC 8.4.3, 9.7.1 (PLAN G705): a Contract nested in a generic Container is named with its outer Type arguments outside that
    // Container in every written position: a parent, an associate selector, a projection qualifier and an effect target. Every one is
    // formed by the one former, whose rule this is, so the effect rows are here and not with the effect owner's records.
    [Theory]
    [InlineData("struct X\n    Self is Outer<i32>.C\n    associate Outer<i32>.C.Item is i32", true)]
    [InlineData("struct X\n    Self is Outer<i32>.C\n    associate Outer.C.Item is i32", false)]
    [InlineData("func f<U>(x: U.(Outer<i32>.C).Item) -> i32\n    U is Outer<i32>.C\n    U.(Outer<i32>.C).Item is i32\n    return x", true)]
    [InlineData("contract D: Outer<i32>.C", true)]
    [InlineData("contract D: Outer.C", false)]
    [InlineData("contract D: Outer<i32>.C\n    associate Outer<i32>.C.Item is i32", true)]
    [InlineData("contract D: Outer.C\n    associate Outer.C.Item is i32", false)]
    [InlineData("contract D: Outer<i32>.C\nstruct S\n    Self is D\n    associate Outer<i32>.C.Item is i32", true)]
    [InlineData("contract D: Outer<i32>.C\nstruct S\n    Self is D\n    associate Outer.C.Item is i32", false)]
    [InlineData("    public contract G<U>\n        associate Item\ncontract D: Outer<i32>.G<i64>", true)]
    [InlineData("    public contract G<U>\n        associate Item\ncontract D: Outer.G<i64>", false)]
    [InlineData("    public contract G<U>\n        associate Item\ncontract D: Outer<i32>.G<i64>\n    associate Outer<i32>.G<i64>.Item is i32", true)]
    [InlineData("        func f(self: ref/Self) -> i32\ncontract D: Outer<i32>.C\n    effect Outer<i32>.C.f confined", true)]
    [InlineData("        func f(self: ref/Self) -> i32\ncontract D: Outer<i32>.C\n    effect Outer.C.f confined", false)]
    public void ANestedContractReferenceTakesItsOuterTypeArguments(string use, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess("struct Outer<T>\n    public contract C\n        associate Item\n" + use);
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    [Theory]
    [InlineData("associate Element is i32", "i32", true)]
    [InlineData("associate C.Element is i32", "i32", true)]
    [InlineData("associate C.Element is string", "i32", false)]
    [InlineData("", "i32", true)]
    [InlineData("associate C.Element is ref/i32 during static", "i32", false)]
    public void AssociatedIdentityUsesExplicitOrBoundedDeclarationEvidence(string specification, string result, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    associate Element\n    func read() -> Element\nstruct S\n    Self is C\n    {specification}\n    public func read() -> {result} => 1");
        Assert.Equal(valid, c.Bind().IsComplete);
        if (valid)
        {
            Assert.Equal("i32", Assert.Single(c.Binding.GetConformance(Container(c, "S").TypeOf()!, Container(c, "C").SymbolOf()!)!.AssociatedTypes).Value.Name);
        }
    }

    [Fact]
    public void ChildIdentityConstraintDeterminesInheritedAssociatedType()
    {
        var c = CompilationTestHelper.ParseSuccess("contract A\n    associate Element\n    func read() -> Element\ncontract B: A\n    Self.(A).Element is i32\nstruct S\n    Self is B\n    public func read() -> i32 => 1");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("Element", false)]
    [InlineData("A.Element", true)]
    public void IndependentSameNamedAssociatedTypesRequireQualification(string name, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract A\n    associate Element\ncontract B\n    associate Element\nstruct S\n    Self is A\n    Self is B\n    associate {name} is i32\n    associate B.Element is string");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void ContradictoryAssociatedSpecificationsFail()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate Element is i32\nstruct S\n    Self is C\n    associate C.Element is string");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssociatedType_Kd);
    }

    [Fact]
    public void VerifiedConformanceDischargesGenericCalls()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f()\nstruct S\n    Self is C\n    public func f() => ()\nfunc use<T>(value: T)\n    T is C\n    ()\nfunc caller(value: S) => use(value@move)");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void GenericDefinitionCannotRelyOnFavorableTypeArguments()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f()\nstruct S<T>\n    Self is C\n    public func f<U>(value: U)\n        U is Copy\n        ()\nfunc caller(value: S<i32>) => ()");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void ProjectionUsesRequireConformanceEvidence()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate Element\nfunc f<T>(value: T.(C).Element) => ()");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void SignatureProjectionsSeeAllLeadingConstraints()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate Element\nfunc f<T>(value: T.(C).Element) -> i32\n    T.(C).Element is i32\n    T is C\n    return value");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void GenericTypeRequirementCallRetainsSelfAndRequirementIdentity()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func empty() -> Self\nfunc make<T>() -> T\n    T is C\n    return T.empty()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.NotNull(call.CallOf());
        Assert.Equal(BoundTypeKind.Parameter, call.CallOf()!.ConformingType!.Kind);
        Assert.Equal(Container(c, "C").SymbolOf()!.Contract!.Requirements[0], new(call.CallOf()!.Target, call.CallOf()!.RequirementContract!));
        Assert.Same(call.CallOf()!.ConformingType, call.TypeOf());
        Assert.Null(call.CallOf()!.Receiver);
    }

    [Theory]
    [InlineData("T.(C).Element", "T is C")]
    [InlineData("i32", "T is C\n    T.(C).Element is i32")]
    [InlineData("i32", "T.(C).Element is i32\n    T is C")]
    public void GenericInstanceRequirementCallsSubstituteAssociatedTypes(string result, string constraints)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    associate Element\n    func read(self: ref/Self) -> Element\nfunc readOne<T>(source: ref/T) -> {result}\n    {constraints}\n    return source.read()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.NotNull(call.CallOf()!.Receiver);
        Assert.Equal(BoundTypeKind.Parameter, call.CallOf()!.ConformingType!.Kind);
    }

    [Fact]
    public void GenericCallsDeduplicateDiamondPathsButNotIndependentRequirements()
    {
        var good = CompilationTestHelper.ParseSuccess("contract A\n    func f()\ncontract B: A\ncontract C: A\nfunc call<T>()\n    T is B and C\n    T.f()");
        Assert.True(good.Bind().IsComplete, Describe(good));
        var bad = CompilationTestHelper.ParseSuccess("contract A\n    func f()\ncontract B\n    func f()\nfunc call<T>()\n    T is A and B\n    T.f()");
        Assert.False(bad.Bind().IsComplete);
        Assert.Contains(bad.Binding.Issues, x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Fact]
    public void ContractNameCannotSupplyAnImplementationType()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f() -> i32\nlet value = C.f()");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void GenericRequirementCallsCheckTheirOwnConstraints()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func echo<T>(value: T) -> T\n        T is Copy\nfunc call<S, U>(value: U) -> U\n    S is C\n    U is Copy\n    return S.echo(value)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var bad = CompilationTestHelper.ParseSuccess("contract C\n    func echo<T>(value: T) -> T\n        T is Copy\nfunc call<S, U>(value: U) -> U\n    S is C\n    return S.echo(value)");
        Assert.False(bad.Bind().IsComplete);
    }

    [Theory]
    [InlineData("(x: ref/i32 during a) -> ref/i32 during a", true)]
    [InlineData("(x: ref/i32) -> ref/i32 during x", true)]
    [InlineData("(x: ref/i32 during static) -> ref/i32 during static", false)]
    public void OriginCorrespondencePreservesInputAndResultContracts(string signature, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    func f(x: ref/i32 during r) -> ref/i32 during r\nstruct S\n    Self is C\n    public func f {signature} => x");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void WeakerResultLifetimeFailsEvenThoughInputKeyMatches()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f(x: ref/i32, y: ref/i32) -> ref/i32 during x\nstruct S\n    Self is C\n    public func f(x: ref/i32, y: ref/i32) -> ref/i32 during y => y");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void SharedInputOriginsCanMeetAtARequirementCall()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f(x: ref/i32 during a, y: ref/i32 during a) -> ref/i32 during a\nfunc call<T>(x: ref/i32, y: ref/i32) -> ref/i32 during (x and y)\n    T is C\n    return T.f(x, y)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal(OriginKind.Intersection, call.TypeOf()!.Origin!.Kind);
    }

    [Fact]
    public void DistinctGenericAssociatedConstraintsCannotChooseAnArbitraryIdentity()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate E\nfunc f<T>(value: T.(C).E)\n    T is C\n    T.(C).E is i32\n    T.(C).E is string\n    ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssociatedType_Kd);
    }

    [Fact]
    public void RefinementRejectsIncompatibleLabelsWithoutAnImplementation()
    {
        var c = CompilationTestHelper.ParseSuccess("contract A\n    func f(x: i32)\ncontract B\n    func f(y: i32)\ncontract C: A, B");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Container(c, "C").StateOf());
    }

    [Fact]
    public void RefinementRejectsContradictoryFixedAssociatedTypesWithoutUses()
    {
        var c = CompilationTestHelper.ParseSuccess("contract A\n    associate E is i32\ncontract B: A\n    Self.(A).E is string");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Container(c, "B").StateOf());
    }

    [Fact]
    public void ARequirementInheritsEnclosingTypeParameters()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "struct Outer<T>\n    contract C\n        func f(x: T)");
        Assert.Empty(TestDiagnostics.Of(c));
        Assert.True(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("T", true)]
    [InlineData("ref/i32 during static", true)]
    public void GenericAssociatedBindingsRetainCompleteTypes(string binding, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    associate E\nstruct S<T>\n    Self is C\n    associate C.E is {binding}");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void OrdinaryConstraintsCanProveAGenericAssociatedCoreBinding()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate E\nstruct S<T>\n    T is i32\n    Self is C\n    associate C.E is T");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    // SPEC 8.6.1, 8.2.3 (PLAN G290): a bound reference whose argument is a projection over the Type's parameter (`B<S>`'s
    // `Iter<S.(HasItem).Item>`) is, for an instance, the reference the projection names: `B<SrcI>` and `B<Src>` (HasMore's refinement
    // binds its Item) are `Iter<i32>` through a direct, refinement, conditional or inherited path and as a projection's qualifier, and
    // `B<SrcI>` is `Addable<i32>` for `a + 2`, `Indexable<Option<i32>>` beside `Indexable<Name>` for an `Option<i32>` key and
    // `B<SrcR>` `Indexable<Range<isize, isize>>` for a range key, and i32 is the builtin `Addable<SrcI.(HasItem).Item>`; `B<U>` is under
    // the use scope's premise `U.(HasItem).Item is i32` and unproven without one (DECISIONS BU-50); `B<SrcU>` is not, nor is it
    // `Iter<u8>` as a qualifier, nor does `B<SrcI>` take an `Option<u8>` key, nor is i32 `Addable<SrcU.(HasItem).Item>`. Before, each
    // valid form was NoApplicableOverload_Kd, UnprovenConstraint_Kd, TypeMismatch_Kd, Unsupported_Kd, AmbiguousBinding_Kd or
    // NotIndexable_Kd.
    [Theory]
    [InlineData(Projected, Uses, null)]
    [InlineData("Self is Seq<S.(HasItem).Item>\n    associate Tag is bool", Uses, null)]
    [InlineData("Self is Iter<S.(HasItem).Item> when S is Copy\n        associate Tag is bool", Uses, null)]
    [InlineData(Projected, "struct D : B<SrcI>\nfunc f(d: ref/D) -> ()\n    use(d)", null)]
    [InlineData(Projected, "func f(a: ref/B<SrcI>) -> ()\n    let t: B<SrcI>.(Iter<i32>).Tag = true", null)]
    [InlineData(Added, "func f(a: ref/B<SrcI>) -> i32\n    return a + 2", null)]
    [InlineData(Projected, Add + "func f() -> i32\n    return add<i32, SrcI>(1, 2)", null)]
    [InlineData(Keyed, NameKey + "func f(m: ref/B<SrcI>, k: Option<i32>) -> i32\n    return m[k]", null)]
    [InlineData(
        "Self is Indexable<S.(HasItem).Item>\n    associate Element is i32\n    var small: i32 = 3\n" +
        "    public func index(self, key: ref/S.(HasItem).Item) -> ref/i32 during self => self.small@ref",
        "struct SrcR\n    Self is HasItem\n    associate HasItem.Item is Range<isize, isize>\nfunc f(m: ref/B<SrcR>) -> i32\n    return m[0..2]",
        null)]
    [InlineData(Projected, "func g<U>(x: ref/B<U>) -> ()\n    U is HasItem\n    U.(HasItem).Item is i32\n    use(x)", null)]
    [InlineData(Projected, "func g<U>(x: ref/B<U>) -> ()\n    U is HasItem\n    use(x)", DiagnosticCode.UnprovenConstraint_Kd)]
    [InlineData(Projected, "func f(a: ref/B<SrcU>) -> ()\n    use(a)", DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData(Projected, "func f(a: ref/B<SrcI>) -> ()\n    let t: B<SrcI>.(Iter<u8>).Tag = true", DiagnosticCode.UnsatisfiedConstraint_Kd)]
    [InlineData(Keyed, NameKey + "func f(m: ref/B<SrcI>, k: Option<u8>) -> i32\n    return m[k]", DiagnosticCode.AmbiguousBinding_Kd)]
    [InlineData(Projected, Add + "func f() -> i32\n    return add<i32, SrcU>(1, 2)", DiagnosticCode.NoApplicableOverload_Kd)]
    public void ABoundReferenceIsTheTypeItsProjectionArgumentNames(string conformance, string use, DiagnosticCode? code)
    {
        var c = CompilationTestHelper.ParseSuccess(
            "contract HasItem\n    associate Item\ncontract HasMore: HasItem\n    associate Elem\n    associate HasItem.Item is Elem\nstruct Src\n" +
            "    Self is HasMore\n    Self is Copy\n    associate HasMore.Elem is i32\nstruct SrcI\n    Self is HasItem\n    Self is Copy\n" +
            "    associate HasItem.Item is i32\nstruct SrcU\n    Self is HasItem\n    associate HasItem.Item is u8\ncontract Iter<T>\n    associate Tag\n" +
            $"contract Seq<T>: Iter<T>\nopen struct B<S>\n    S is HasItem\n    {conformance}\nfunc use<T>(x: ref/T) -> ()\n    T is Iter<i32>\n    ()\n{use}");
        Assert.True(c.Bind().IsComplete == (code is null), Describe(c));
        if (code is { } expected)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == expected);
        }
    }

    [Theory]
    [InlineData("public", "public", "internal", false)]
    [InlineData("internal", "public", "internal", true)]
    [InlineData("public", "internal", "internal", true)]
    [InlineData("public", "public", "public", true)]
    public void WitnessAccessCoversTheEffectiveConformanceDomain(string typeAccess, string contractAccess, string memberAccess, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"{contractAccess} contract C\n    func f()\n{typeAccess} struct S\n    Self is C\n    {memberAccess} func f() => ()");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmContractBindingReusesMetadataAndCallStorage()
    {
        var source = "contract A\n    associate E\n    func read(self: ref/Self) -> E\ncontract B: A\n    Self.(A).E is i32\nstruct S\n    Self is B\n    public func read(self: ref/Self) -> i32 => 1\nfunc use<T>(x: ref/T)\n    T is B\n";
        source += string.Concat(Enumerable.Repeat("    x.read()\n", 128));
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void RebindingChecksNewMembersInsteadOfReusingAVerifiedMapping()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f()\nstruct S\n    Self is C\n    public func f() => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var mapping = c.Binding.GetConformance(Container(c, "S").TypeOf()!, Container(c, "C").SymbolOf()!)!;
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "struct S\n    public func f() => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.False(mapping.IsVerified);
    }

    [Fact]
    public void QualifiedContractNamesUseTheSameRegistrationAndProjectionRules()
    {
        var c = CompilationTestHelper.ParseSuccess("group P\n    public contract C\n        associate E\n        func f() -> E\nstruct S\n    Self is P.C\n    associate P.C.E is i32\n    public func f() -> i32 => 1\nfunc call<T>() -> T.(P.C).E\n    T is P.C\n    return T.f()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void AssociatedTypeNamesCannotBeSilentlyHiddenByAChildDeclaration()
    {
        var c = CompilationTestHelper.ParseSuccess("contract A\n    associate E\ncontract B: A\n    associate E\n    func f() -> E");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    // SPEC 23.1.2: the cyclic specifications are invalid, and the conformance's completeness rests on them (G34-8).
    [Fact]
    public void AssociatedIdentityCyclesCannotValidateAnImplementation()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate E\n    associate F\nstruct S\n    Self is C\n    associate C.E is S.(C).F\n    associate C.F is S.(C).E");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidConstraint_Kd && x.Node.ToString() == "associate C.E is S.(C).F");
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssociatedType_Kd);
    }

    [Fact]
    public void PublicConformanceCannotExposePrivateAssociatedTypes()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Hidden\npublic contract C\n    associate E\npublic struct S\n    Self is C\n    associate C.E is Hidden");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InaccessibleBinding_Kd);
    }

    [Fact]
    public void PublicRequirementsCannotExposePrivateTypes()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Hidden\npublic contract C\n    func f() -> Hidden");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InaccessibleBinding_Kd);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void RefiningCopyStillRequiresItsIntrinsicDerivation(string field, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C: Copy\nstruct S\n    Self is C\n    Self is Copy\n    let value: {field}");
        Assert.Equal(valid, c.Bind().IsComplete);
        if (valid)
        {
            Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(Container(c, "S").TypeOf()!, Container(c, "S")));
        }
    }

    [Fact]
    public void RefinementPremisesProveIntrinsicRequirementsInGenericBodies()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C: Copy\nfunc copy<T>(x: T) -> T\n    T is Copy\n    return x\nfunc use<T>(x: T) -> T\n    T is C\n    return copy(x)");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("value => left", "value => right", true)]
    [InlineData("left", "right", false)]
    public void IdentificationUsesExternalLabelsButNotInternalNames(string a, string b, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    func f({a}: i32)\nstruct S\n    Self is C\n    public func f({b}: i32) => ()");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void GenericSlotKindsMatterToWitnessIdentification()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f<s/T>(x: s/T)\nstruct S\n    Self is C\n    public func f<T>(x: T) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.MissingContractImplementation_Kd);
    }

    [Fact]
    public void ImplementationDefaultsDoNotChangeTheRequirementCallContract()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f(x: i32) -> i32\nstruct S\n    Self is C\n    public func f(x: i32 = 1) -> i32 => x\nfunc call<T>() -> i32\n    T is C\n    return T.f()");
        Assert.False(c.Bind().IsComplete);
        Assert.NotNull(c.Binding.GetConformance(Container(c, "S").TypeOf()!, Container(c, "C").SymbolOf()!));
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }

    // SPEC 15.3.6: a requirement's result-only Origin never escapes as the requirement's abstract binder; at the call it is a local
    // region of that call.
    [Fact]
    public void ResultOnlyOriginsCannotEscapeAsUnsubstitutedRequirementBinders()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f() -> ref/i32 during a\nfunc call<T>()\n    T is C\n    T.f()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.NotNull(call.CallOf());
        var origin = call.TypeOf()!.Origin!;
        Assert.Equal((OriginKind.Inference, true), (origin.Kind, origin.Open));
        Assert.Same(call, origin.Binder);
    }

    [Fact]
    public void GeneratedMembersCanCompleteAProvisionalConformance()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f()\nstruct S\n    Self is C");
        c.Binding.Bind(BindingMode.Provisional);
        var shape = Container(c, "C").SymbolOf()!.Contract;
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "struct S\n    public func f() => ()");
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        Assert.Same(shape, Container(c, "C").SymbolOf()!.Contract);
        var mapping = c.Binding.GetConformance(Container(c, "S").TypeOf()!, Container(c, "C").SymbolOf()!)!;
        Assert.Same(mapping.Witnesses[0].Implementation, mapping.GetImplementation(mapping.Witnesses[0].Identity));
    }

    [Fact]
    public void ReferencedNestedAssociatedContractsSupplyTheirFixedIdentities()
    {
        var c = CompilationTestHelper.ParseSuccess("contract D\n    associate F is i32\ncontract C\n    associate E is D\nfunc f<T>(value: T.(C).E.(D).F) -> i32\n    T is C\n    return value");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void APrivateChildCannotNarrowItsPublicAncestorsConformanceDomain()
    {
        var c = CompilationTestHelper.ParseSuccess("public contract A\n    func f()\ncontract B: A\npublic struct S\n    Self is B\n    internal func f() => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void ConditionalCopyUsesTheSameRefinementPremiseExpansion()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C: Copy\nstruct S<T>\n    Self is Copy when T is C\n    let value: T");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void WitnessPremisesUseRefinementWithoutLeakingToTheImplementationBody()
    {
        var c = CompilationTestHelper.ParseSuccess("contract A: Copy\ncontract C\n    func f<T>(x: T)\n        T is A\nstruct S\n    Self is C\n    public func f<U>(x: U)\n        U is Copy\n        ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    // SPEC 8.6.3, 8.4.2: a requirement's own identity premise is a premise for compatibility, so the implementation's slots correspond
    // to the requirement's before it normalizes either side; identification never reads it (PLAN G679).
    [Theory]
    [InlineData(PutAtI32 + "U) => ()", "")]
    [InlineData(PutAtI32 + "U)\n        U is i32\n        return", "")]
    [InlineData(PutAtI32 + "U)\n        U is bool\n        return", "IncompatibleContractImplementation_Kd")]
    [InlineData(PutAtI32 + "i32) => ()", "MissingContractImplementation_Kd")]
    [InlineData(PutAtE + "E\n        return", "")]
    [InlineData(PutAtE + "i32\n        return", "UnprovenConstraint_Kd")]
    [InlineData("contract Put\n    func put<U>(self: uniq/Self, u: U)\n        U is i32\nstruct S\n    Self is Put\n    public func put<U>(self: uniq/Self, u: U)\n        U is i32\n        return", "")]
    [InlineData(PutUW + " => ()", "")]
    [InlineData(PutUW + "\n        U is i32\n        return", "UnprovenConstraint_Kd")]
    [InlineData(GetU + "i32 => 0", "")]
    [InlineData(GetU + "bool => true", "IncompatibleContractImplementation_Kd")]
    public void RequirementIdentityPremisesJudgeCorrespondingSlots(string source, string codes) => AssertBindingCodes(source, codes);

    // SPEC 8.4.1: `Self` in a Contract's requirement denotes the conforming Type, never the Contract, so `U is Self` is an identity
    // premise (PLAN G701); a conformance supplies no evidence before its implementations are compatible (8.3.2, 8.3.3). A bound
    // Contract's requirement reads the same (`Put<X>`), and so do a Contract-wide identity of an associated Type and a Contract nested
    // in a struct, whose `Self` is the conformer and not the struct (6.1.4). A call through the requirement proves `U is Self` for the
    // receiver's Type only (8.2.3; the last rows).
    [Theory]
    [InlineData(PutSelf + "S\n        return", "")]
    [InlineData(PutSelf + "Self\n        return", "")]
    [InlineData(PutSelf + "i32\n        return", "IncompatibleContractImplementation_Kd")]
    [InlineData("struct R\n" + PutSelf + "R\n        return", "IncompatibleContractImplementation_Kd")]
    [InlineData("contract Q\n" + PutSelf + "Q\n        return", "IncompatibleContractImplementation_Kd")]
    [InlineData(PutSelf + "Put\n        return", "UnprovenConstraint_Kd")]
    [InlineData("contract C\n    associate E is Self\nstruct S\n    Self is C\n    associate E is S", "")]
    [InlineData("contract C\n    associate E is Self\nstruct S\n    Self is C\n    associate E is i32", "InvalidAssociatedType_Kd")]
    [InlineData("contract Put<X>\n    func put<U>(self: uniq/Self, u: U, x: X)\n        U is Self\nstruct S\n    Self is Put<i32>\n    public func put<U>(self: uniq/Self, u: U, x: i32)\n        U is Self\n        return", "")]
    [InlineData("contract Put<X>\n    func put<U>(self: uniq/Self, u: U, x: X)\n        U is Self\nstruct S\n    Self is Put<i32>\n    public func put<U>(self: uniq/Self, u: U, x: i32)\n        U is Put<i32>\n        return", "UnprovenConstraint_Kd")]
    [InlineData("struct S\n    contract C\n        func put<U>(self: ref/Self, u: U)\n            U is Self\n    Self is C\n    public func put<U>(self: ref/Self, u: U)\n        U is S\n        return", "")]
    [InlineData("contract Put\n    func put<U>(self: ref/Self, u: U) -> i32\n        U is Self\nfunc call<T>(a: ref/T, b: T) -> i32\n    T is Put\n    return a.put(b@move)", "")]
    [InlineData("contract Put\n    func put<U>(self: ref/Self, u: U) -> i32\n        U is Self\nfunc call<T, V>(a: ref/T, b: V) -> i32\n    T is Put\n    V is Put\n    return a.put(b@move)", "UnprovenConstraint_Kd")]
    public void RequirementSelfNamesTheConformingType(string source, string codes) => AssertBindingCodes(source, codes);

    // SPEC 8.6.3, 15.3.7, 15.6.1: implementation matching is a whole-contract comparison, so its proofs assume the own well-formedness of
    // the requirement as substituted: under `Self` = `View{source}`, `self: ref/Self` gives `source outlives self`, so a result or getter
    // during `source` gives at least the required guarantee, as Slice's `index` does (SPEC 4.7.1). The premise relates `source` to `self`
    // alone: never to `static`, to another input or to a requirement without a receiver, and never the reverse.
    [Theory]
    [InlineData("contract L\n    func lend(self: ref/Self) -> ref/i32 during self\n" + ViewIsL + "    public func lend(self: ref/Self) -> ref/i32 during self.source => self.data", "")]
    [InlineData("struct W<T> {source}\n    Self is Indexable<isize>\n    associate Element is T\n    let data: ref/T during source\n    public func index(self: ref/Self, key: ref/isize) -> ref/T during source => self.data", "")]
    [InlineData("contract L\n    property item: ref/i32\n        get(self: ref/Self) -> ref/i32 during self\n" + ViewIsL + "    public computed item: ref/i32 during source\n        get(self: ref/Self) -> ref/i32 during self.source => self.data", "")]
    [InlineData("contract L\n    func lend(self: ref/Self) -> ref/i32 during static\n" + ViewIsL + "    public func lend(self: ref/Self) -> ref/i32 during self.source => self.data", "IncompatibleContractImplementation_Kd")]
    [InlineData("contract L\n    func lend(self: ref/Self, other: ref/i32) -> ref/i32 during other\n" + ViewIsL + "    public func lend(self: ref/Self, other: ref/i32) -> ref/i32 during self.source => self.data", "IncompatibleContractImplementation_Kd")]
    [InlineData("contract L\n    func make(x: ref/i32) -> ref/i32 during x\n" + ViewIsL + "    public func make(x: ref/i32) -> ref/i32 during source => $abort(\"none\")", "IncompatibleContractImplementation_Kd")]
    [InlineData("contract L\n    func take(self: ref/Self, x: ref/i32 during self) -> ()\n" + ViewIsL + "    public func take(self: ref/Self, x: ref/i32 during self.source) -> () => ()", "IncompatibleContractImplementation_Kd")]
    [InlineData("contract L\n    property item: ref/i32 during static\n        get(self: ref/Self) -> ref/i32 during static\n" + ViewIsL + "    public computed item: ref/i32 during source\n        get(self: ref/Self) -> ref/i32 during self.source => self.data", "IncompatibleContractImplementation_Kd")]
    public void ResultOriginsAssumeTheSubstitutedRequirementWellFormed(string source, string codes) => AssertBindingCodes(source, codes);

    // SPEC 8.4.3, 8.7.1: a compiler-intrinsic Contract the capability engine judges is a refinement parent; a conformance to the child
    // entails the parent by the parent's own judgment (SPEC 8.7.2 to 8.7.4), and `T is C` supplies it with what it supplies (PLAN G717).
    // A Contract that requires Copy, as a parent or a clause, never opts a conforming Type in: the Type's own `Self is Copy` does (3.5.2).
    [Theory]
    [InlineData("contract C: PrimitiveInteger", "")]
    [InlineData("contract C: Kimi.PrimitiveInteger", "")]
    [InlineData("contract D\ncontract C: D, PrimitiveInteger", "")]
    [InlineData("contract B: PrimitiveInteger\ncontract C<K>: B", "")]
    [InlineData("contract C: Sealed", "")]
    [InlineData("contract C: Callable", "InvalidConstraint_Kd")] // SPEC 8.7.1: Callable is written only as Callable<r, S> or Callable<S> in a Constraint.
    [InlineData("contract C: Callable<(i32) -> i32>", "InvalidConstraint_Kd")]
    [InlineData("func f<F>(g: F) -> F\n    F is Callable\n    return g@move", "InvalidConstraint_Kd")]
    [InlineData("contract C: PrimitiveInteger, Nope", "InvalidConstraint_Kd")]
    [InlineData("contract C: Copy\nstruct S\n    Self is C\n    var x: i32", "UnsatisfiedConstraint_Kd")]
    [InlineData("contract C\n    Self is Copy\nstruct S\n    Self is C\n    var x: i32", "UnsatisfiedConstraint_Kd")]
    [InlineData("contract B: Copy\ncontract C: B\nenum E\n    Self is C\n    A", "UnsatisfiedConstraint_Kd")]
    [InlineData("contract B: Copy\ncontract C: B\nenum E\n    Self is Copy\n    Self is C\n    A", "")]
    [InlineData("contract C: Copy\nstruct S<T>\n    Self is Copy when T is Copy\n    Self is C when T is Copy\n    var x: T", "")]
    [InlineData("contract C: Copy\nstruct S<T>\n    Self is C when T is Copy\n    var x: T", "UnsatisfiedConstraint_Kd")]
    [InlineData("contract C: Copy\nstruct S<T>\n    Self is Copy when T is Copy\n    Self is C\n    var x: T", "UnprovenConstraint_Kd")]
    [InlineData("contract C: Copy\nopen struct A\n    Self is Copy\n    var x: i32\nstruct D: A\n    Self is C", "UnsatisfiedConstraint_Kd")]
    [InlineData("contract C: ObjectPayload\nstruct S<T>\n    Self is not ObjectPayload\n    var x: T\n    Self is C when T is Copy", "UnsatisfiedConstraint_Kd")]
    [InlineData("contract C: ObjectPayload\nopen struct A\n    Self is not ObjectPayload\n    var x: i32\nstruct D: A\n    Self is C", "UnsatisfiedConstraint_Kd")]
    [InlineData("contract C: PrimitiveInteger\nfunc at<T>(xs: ref/Array<i32>, i: T) -> i32\n    T is C\n    return xs[i]", "")]
    [InlineData("contract C: PrimitiveInteger\nfunc wide<T>(a: T) -> i64\n    T is C\n    return a@i64", "")]
    [InlineData("contract C: PrimitiveInteger\nstruct S\n    Self is C\n    var x: i32", "UnsatisfiedConstraint_Kd")]
    [InlineData("contract B: PrimitiveInteger\ncontract C: B\nenum E\n    Self is C\n    A", "UnsatisfiedConstraint_Kd")]
    [InlineData("contract C: ObjectPayload\nstruct S\n    Self is C\n    var x: i32", "")]
    [InlineData("contract C: ObjectPayload\nstruct S\n    Self is not ObjectPayload\n    Self is C\n    var x: i32", "UnsatisfiedConstraint_Kd")]
    [InlineData("contract B: ObjectPayload\ncontract C: B\nstruct S\n    Self is not ObjectPayload\n    Self is C\n    var x: i32", "UnsatisfiedConstraint_Kd")]
    [InlineData("contract C: PrimitiveInteger\n" + NeedOf + "PrimitiveInteger" + UseOfC, "")]
    [InlineData("contract B: PrimitiveInteger\ncontract C: B\n" + NeedOf + "PrimitiveInteger" + UseOfC, "")]
    [InlineData("contract C\n" + NeedOf + "PrimitiveInteger" + UseOfC, "UnprovenConstraint_Kd")]
    [InlineData("contract C: PrimitiveInteger\n" + NeedOf + "Copy" + UseOfC, "")]
    [InlineData("contract B: PrimitiveInteger\ncontract C: B\n" + NeedOf + "Owned" + UseOfC, "")]
    [InlineData("contract C\n" + NeedOf + "Copy" + UseOfC, "UnprovenConstraint_Kd")]
    [InlineData("contract C: PrimitiveInteger\nfunc add<T>(a: T, b: T) -> T\n    T is C\n    return a + b", "")]
    [InlineData("contract C\nfunc add<T>(a: T, b: T) -> T\n    T is C\n    return a + b", "ArithmeticSelection_Kd")]
    [InlineData("contract C: PrimitiveInteger\nfunc same<T>(a: T, b: T) -> bool\n    T is C\n    return a == b", "")]
    [InlineData("contract C: PrimitiveInteger\nfunc zero<T>() -> T\n    T is C\n    return 0", "")]
    public void JudgedIntrinsicParentsRefineOrdinarily(string source, string codes) => AssertBindingCodes(source, codes);

    // Whether `source` binds completely exactly when `codes` is empty, with these distinct issue codes.
    private static void AssertBindingCodes(string source, string codes)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.Equal(codes.Length == 0, c.Bind().IsComplete);
        Assert.Equal(codes, string.Join(" ", c.Binding.Issues.Select(static x => x.Code.ToString()).Distinct().Order()));
    }

    private static DeclarationContainerKoto Container(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<DeclarationContainerKoto>().Single(x => x.Name == name);

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code} ({x.Node.GetType().Name}, {x.Node.Akind}, {x.Node.Parent?.GetType().Name}): {x.Node}"));
}
