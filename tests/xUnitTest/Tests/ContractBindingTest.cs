// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ContractBindingTest
{
    private const string Labels = "    func f(self: ref/Self, x: i32) -> i32\n    func f(self: ref/Self, y: i32) -> i32\n";
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

    // PLAN G949 S1-b1: three Contracts with one requirement each (Sources adds Types of their own sources conforming to them).
    private const string ThreeContracts = "contract C\n    func get(self: ref/Self) -> i32\ncontract D\n    func get(self: ref/Self) -> i32\n" +
        "contract E\n    func get(self: ref/Self) -> i32\n";

    private const string NeedOf = "func need<T>(v: T) -> T\n    T is ";
    private const string UseOfC = "\n    return v@move\nfunc use<T>(v: T) -> T\n    T is C\n    return need(v@move)";

    private const string PutOfT = "contract Put<T>\n    func put<U>(self: uniq/Self, u: U)\n        U is T\n";
    private const string PutAtI32 = PutOfT + "struct S\n    Self is Put<i32>\n    public func put<U>(self: uniq/Self, u: ";
    private const string PutAtE = PutOfT + "struct Box<E>\n    Self is Put<E>\n    public func put<U>(self: uniq/Self, u: U)\n        U is ";
    private const string PutUW = "contract Put\n    func put<U, W>(self: uniq/Self, u: U, w: W)\n        U is W\nstruct S\n    Self is Put\n    public func put<U, W>(self: uniq/Self, u: U, w: W)";
    private const string GetU = "contract Get<T>\n    func get<U>(self: ref/Self, u: U) -> U\n        U is T\nstruct S\n    Self is Get<i32>\n    public func get<U>(self: ref/Self, u: U) -> ";
    private const string PutSelf = "contract Put\n    func put<U>(self: uniq/Self, u: U)\n        U is Self\nstruct S\n    Self is Put\n    public func put<U>(self: uniq/Self, u: U)\n        U is ";
    private const string ViewIsL = "struct View {source}\n    Self is L\n    let data: ref/i32 during source\n";

    // C2's clause supplies C2, which refines Named, two Tree steps down: at heights 2 and 4 from a `T is C2`, never at 3.
    private const string TreeC2 = "contract Tree\n    associate Child is Tree\ncontract Named\n    func name(self: ref/Self) -> i32\n";
    private const string C2 = "contract C2: Tree, Named\n    Self.(Tree).Child.(Tree).Child is C2\n";
    private const string Height4 = "T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child";

    // PeekTree's clause proves PeekTree one Tree step down from each row, so at height 2 from a `T is PeekTree` (the Middle's twins).
    private const string PeekTree = "contract Tree\n    associate Child is Tree\ncontract PeekTree: Tree, Copy\n    Self.(Tree).Child is PeekTree\n" +
        "    func peek(self: ref/Self) -> ref/i32 during self\n";

    private const string PeekProbe = "func probe<T>(a: T.(Tree).Child.(Tree).Child, b: T.(Tree).Child.(Tree).Child) -> i32\n    T is PeekTree\n    var v = a\n";
    private const string PeekMain = "public func main() -> ()\n    Console.writeLine(\"ok\")\n";

    private const string PeekStore = "func put<V>(src: ref/V, dst: uniq/(Option<ref/V during src>))\n    V is PeekTree\n    dst@follow = .Some(src)\n" + PeekProbe +
        "    var slot: Option<ref/T.(Tree).Child.(Tree).Child> = .None\n    put<T.(Tree).Child.(Tree).Child>(v@ref, slot@uniq)\n    match slot\n" +
        "        .Some(let s) => return s.peek()\n        .None => return 0\n";

    // A depth-3 chain of clause rows (CL1 -> CL2 -> CL3) used by a member call, a copy, a capture and an erasure, instantiated with S1:
    // DepthThreeChain ends at `cap<T>`'s clause `T is CL1`; DepthThreeCap is `cap`'s body, which erases a Closure formed in it, and main.
    private const string DepthThreeChain = "contract L3\n    associate Item\ncontract CL3: L3\n    Self.(L3).Item is Copy\n    Self.(L3).Item is Owned\n" +
        "    func get(self: ref/Self) -> i32\ncontract L2\n    associate Inner is L3\ncontract CL2: L2\n    Self.(L2).Inner is CL3\ncontract L1\n" +
        "    associate Mid is L2\ncontract CL1: L1\n    Self.(L1).Mid is CL2\nstruct P\n    Self is Copy\n    public let x: i32\n" +
        "    public init(x: i32) => self.x = x\nstruct S3\n    Self is CL3\n    associate L3.Item is P\n    public let k: i32\n" +
        "    public init(k: i32) => self.k = k\n    public func get(self: ref/Self) -> i32 => self.k\nstruct S2\n    Self is CL2\n" +
        "    associate L2.Inner is S3\nstruct S1\n    Self is CL1\n    associate L1.Mid is S2\nfunc need<V>(x: V) -> i32\n    V is Owned\n    return 1\n" +
        "func keep<V>(x: ref/V) -> i32\n    V is CL3\n    return x.get()\n" +
        "func dup<T>(v: T.(L1).Mid.(L2).Inner.(L3).Item) -> (T.(L1).Mid.(L2).Inner.(L3).Item, T.(L1).Mid.(L2).Inner.(L3).Item)\n    T is CL1\n" +
        "    return (v, v)\nfunc read<T>(v: T.(L1).Mid.(L2).Inner) -> i32\n    T is CL1\n    let n = keep<T.(L1).Mid.(L2).Inner>(v@ref)\n" +
        "    return n + v.get()\nfunc cap<T>(v: T.(L1).Mid.(L2).Inner.(L3).Item) -> i32\n    T is CL1";

    private const string DepthThreeCap = "\n    let c = func () -> (T.(L1).Mid.(L2).Inner.(L3).Item, T.(L1).Mid.(L2).Inner.(L3).Item) => (v, v)\n    let d = c\n    let p = c()\n" +
        "    let q = d()\n    let f: () -> (T.(L1).Mid.(L2).Inner.(L3).Item, T.(L1).Mid.(L2).Inner.(L3).Item) = c\n    let s = f()\n" +
        "    return need(v) + need(s.0)\npublic func main() -> ()\n    let p = dup<S1>(P.init(x: 3))\n    let n = read<S1>(S3.init(5))\n" +
        "    let m = cap<S1>(P.init(x: 4))\n    if p.0.x + p.1.x == 6 and n == 10 and m == 2 => Console.writeLine(\"ok\")\n    else => Console.writeLine(\"bad\")\n";

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

    // R7-E2 E2-5a (the Front's B1; probes a1, a2, q1, labelled from SPEC first): a refinement's reference without its Type arguments
    // (9.7.1) and a specification's Name ambiguous among its owner's conformances (8.5.2) are each one failed requirement at that node;
    // its verdict that nothing was formed rests on it and adds no record (the slot's first failure until R7-E5, D-924). Regression rows
    // for the records E2-5a's first commit published there, as `Code: span`; a2 keeps dev's records, a dependent one at `K<i32>` among them
    // (D-924's E5 row); the twins are valid.
    [Theory]
    [InlineData("a1", "contract K<T>\n    func k(self: ref/Self) -> i32\ncontract A: K", "InvalidConstraint_Kd: contract | TypeMismatch_Kd: K")]
    [InlineData("a1t", "contract K<T>\n    func k(self: ref/Self) -> i32\ncontract A: K<i32>", "")]
    [InlineData("a2", "contract K\n    func k(self: ref/Self) -> i32\ncontract A: K<i32>", "InvalidConstraint_Kd: K<i32> | InvalidConstraint_Kd: contract | TypeMismatch_Kd: K<i32>")]
    [InlineData("q1", "contract C<E>\n    associate Item\nstruct S\n    Self is C<i32>\n    Self is C<i64>\n    associate Item is i32", "AmbiguousBinding_Kd: Item | AssociatedTypeInferenceFailed_Kd: Self is C<i32> | AssociatedTypeInferenceFailed_Kd: Self is C<i64>")]
    [InlineData("q1t", "contract C<E>\n    associate Item\nstruct S\n    Self is C<i32>\n    associate Item is i32", "")]
    public void AReferenceThatFormedNothingAddsNoRecordOfItsOwn(string form, string program, string records)
    {
        var source = program + "\npublic func main() => ()";
        var errors = CompilationTestHelper.BindingErrors(MinimalEmissionTest.Analyze(source));
        var actual = string.Join(" | ", errors.Select(x => $"{x.Code}: {source.Substring(x.Span.Start, x.Span.Length)}").Order(StringComparer.Ordinal));
        Assert.True(records == actual, $"{form}: {actual}");
    }

    // R7-E2 E2-5a (the Front's C-a, M1; probes k1, k1t, k2, labelled from SPEC first): two requirements of one signature that differ
    // only by labels are a Contract's own failed requirement (SPEC 8.4.3), read from its declared requirements, so it is its own record
    // at the Contract beside another requirement's failure there: a public Contract exposing a private parent (9.4.4, k1) or a refinement
    // cycle (8.4.3, k2); dev kept the first only. k1t, the parent public, gives that record alone.
    [Theory]
    [InlineData("k1", "contract B\n    func b(self: ref/Self) -> i32\npublic contract A: B\n" + Labels, "DuplicateBinding_Kd: func f(self: ref/Self, y: i32) -> i32 | InaccessibleBinding_Kd: public contract A: B | InvalidConstraint_Kd: public contract A: B")]
    [InlineData("k1t", "public contract B\n    func b(self: ref/Self) -> i32\npublic contract A: B\n" + Labels, "DuplicateBinding_Kd: func f(self: ref/Self, y: i32) -> i32 | InvalidConstraint_Kd: public contract A: B")]
    [InlineData("k2", "contract A: B\n" + Labels + "contract B: A", "CyclicBinding_Kd: contract A: B | DuplicateBinding_Kd: func f(self: ref/Self, y: i32) -> i32 | InvalidConstraint_Kd: contract A: B | InvalidConstraint_Kd: contract B: A")]
    public void AContractsLabelConflictIsItsOwnRecordBesideAnotherFailure(string form, string program, string records)
        => CompilationTestHelper.AssertBindingRecordsAtLines(form, program + "\npublic func main() => ()", records);

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
    [InlineData("contract C\n    associate Item\ncontract D: C\nstruct S\n    Self is C\n    associate (D).Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\nstruct S\n    Self is C\n    associate D.Item is i32", false)]
    [InlineData("struct C\n    let v: i32 = 0\ncontract D\n    associate Item\nstruct S\n    Self is D\n    associate (C).Item is i32", false)]
    [InlineData("contract C\n    associate Item\nenum E\n    A\n    associate (C).Item is i32", false)]
    [InlineData("contract C\n    associate Item\nenum E\n    A\n    Self is C\n    associate Self.Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\nenum E\n    A\n    Self is C\n    associate (D).Item is i32", false)]
    [InlineData("contract C\n    associate Item\ncontract K\n    associate Item\nstruct W<T>\n    Self is C when T is Copy\n        associate (K).Item is T", false)]
    [InlineData("contract C\n    associate Item\ncontract D: C\nstruct W<T>\n    Self is C when T is Copy\n        associate (D).Item is T", false)]
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
    [InlineData("func one<T>(source: ref/T) -> T.(::C).Item\n    T is C\n    return source.get()", true)]
    [InlineData("func f<T>(x: ref/T) -> i32\n    T is C\n    T.(C).Item is i32\n    return 0", true)]
    [InlineData("contract D: C\n    Self.(C).Item is i32", true)]
    public void AProjectionsContractIsOneGroupedReference(string use, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate Item\n    func get(self: ref/Self) -> Self.Item\n" + use);
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    // SPEC F.6.2 (PB3, F's ruling 16): a grouped Contract holds a ContractReference, a NamedReference, so a group inside it, `((C))`, in an
    // associate selector or a `.(C).` suffix is Parsing's record at the inner group (ContractReference); Binding rejected it on base.
    [Theory]
    [InlineData("contract C\n    associate Item\ncontract D: C\n    associate ((C)).Item is i32")]
    [InlineData("contract C\n    associate Item\nstruct S\n    Self is C\n    associate ((C)).Item is i32")]
    [InlineData("contract C\n    associate Item\nenum E\n    A\n    Self is C\n    associate ((C)).Item is i32")]
    [InlineData("contract C\n    associate Item\nstruct W<T>\n    Self is C when T is Copy\n        associate ((C)).Item is T")]
    [InlineData("contract C\n    associate Item\n    func get(self: ref/Self) -> Self.Item\nfunc one<T>(source: ref/T) -> T.((C)).Item\n    T is C\n    return source.get()")]
    [InlineData("contract C\n    associate Item\n    func get(self: ref/Self) -> Self.Item\nfunc f<T>(x: ref/T) -> i32\n    T is C\n    T.((C)).Item is i32\n    return 0")]
    [InlineData("contract C\n    associate Item\n    func get(self: ref/Self) -> Self.Item\ncontract D: C\n    Self.((C)).Item is i32")]
    public void AGroupInAGroupedContractIsOneParsingRecord(string source)
    {
        var record = Assert.Single(TestDiagnostics.Of(CompilationTestHelper.Parse(source, "Hello.kimi"), "Hello.kimi"));
        Assert.Equal(nameof(DiagnosticCode.ExpectedSyntax_Kd), record.Code);
        Assert.Contains("Expected contract reference", record.Message, StringComparison.Ordinal);
        Assert.Equal(source.IndexOf("((C))", StringComparison.Ordinal) + 1, record.Span.Start);
        Assert.Equal(3, record.Span.Length);
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

    // PLAN D-942 (R7-N5 B3-2; SPEC 9.6, 23.1.2; DIAGNOSTICS rule 3): a member access's requirement group is its pass's working state.
    // An edit that replaces only `x`'s Type node (`ref/T` under `T is Reader` -> `ref/i32`) leaves `x.size` unlooked-up through
    // requirements; the edited Bind reports what a fresh compilation of the edited text reports: the missed Name, not the Property
    // requirement's limit an earlier pass's group would answer.
    [Fact]
    public void AnEditedReceiverReadsNoEarlierPasssRequirementGroup()
    {
        const string Reader = "contract Reader\n    property size: i32 has get\n";
        var c = CompilationTestHelper.Parse(Reader + "func f<T>(x: ref/T) -> i32\n    T is Reader\n    return x.size\n", "Hello.kimi");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var donor = KotoTree.Walk(CompilationTestHelper.ParseSuccess("func g(x: ref/i32) -> i32 => 1").Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "g").Parameters[0].Type;
        ParseTestHelper.Detach(donor, new CodeBlockKoto(donor.CodeContext));
        Assert.True(KotoHelper.Replace(f, f.Parameters[0].Type, donor));
        Assert.False(c.Bind().IsComplete);
        var fresh = CompilationTestHelper.Parse(Reader + "func f<T>(x: ref/i32) -> i32\n    T is Reader\n    return x.size\n", "Hello.kimi");
        Assert.False(fresh.Bind().IsComplete);
        Assert.Equal([(DiagnosticCode.UnresolvedBinding_Kd, "x.size")], fresh.Binding.Issues.Select(static x => (x.Code, x.Node.ToString())));
        Assert.Equal(fresh.Binding.Issues.Select(static x => (x.Code, x.Node.ToString())), c.Binding.Issues.Select(static x => (x.Code, x.Node.ToString())));
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

    // SPEC 9.4.4, 23.1.2, 23.3 (R7-E2 E2-4a; probes a1, a3m, b3a): each (declaration, inaccessible T) pair is its own failed requirement,
    // recorded at the part that shows T (a requirement's parameter or result) and relating T's declaration: the requirement's two pairs
    // are two records (M1), as are a Contract's two inaccessible ancestors, each located at the Contract (its base references are
    // BuildContract's, R7-N2a) and relating its ancestor; a public conformance's binding exposing a private Type is located at the
    // conformance and relates it. The twins name public declarations and are valid.
    [Theory]
    [InlineData("struct H1\nstruct H2\npublic contract C\n    func f(value: H1) -> H2\n", "InaccessibleBinding_Kd 4:19 H1, inaccessible declaration 'H1'|InaccessibleBinding_Kd 4:26 H2, inaccessible declaration 'H2'")] // a1
    [InlineData("public struct H1\npublic struct H2\npublic contract C\n    func f(value: H1) -> H2\n", "")] // a1t
    [InlineData("contract H1\ncontract H2\npublic contract C: H1, H2\n", "InaccessibleBinding_Kd 3:8 contract, inaccessible declaration 'H1'|InaccessibleBinding_Kd 3:8 contract, inaccessible declaration 'H2'")] // a3m
    [InlineData("public contract H1\npublic contract C: H1\n", "")] // a3t
    [InlineData("struct Hidden\npublic contract C\n    associate E\npublic struct S\n    Self is C\n    associate C.E is Hidden\n", "InaccessibleBinding_Kd 5:5 Self is C, inaccessible declaration 'Hidden'")] // b3a
    [InlineData("public struct Hidden\npublic contract C\n    associate E\npublic struct S\n    Self is C\n    associate C.E is Hidden\n", "")] // b3t
    public void EachInaccessibleDeclarationOfAContractIsItsOwnRecord(string source, string records)
        => Assert.Equal(records, string.Join('|', DiagnosticCorpus.LocatedRecords(source + "public func main() => ()\n")));

    // SPEC 9.4, 9.5, 23.1.2 (PLAN G1207; R7-E2 E2-4b, probes x1, x1s, x1t, x2): a parent no lookup selects is the resolver's miss, explained
    // by an inaccessible qualifier where one is: a qualified parent naming a private Contract or struct records InaccessibleBinding_Kd at
    // its reference relating it, a missing one UnresolvedBinding_Kd; the Contract keeps its own record over its failed parent. Before E2-4b
    // the reference recorded InvalidConstraint_Kd (another requirement's record). The twin is valid. The miss sits where a Type use's does
    // (C-405 C1, probes x5, x2g with their Type-position twins x5t, x2gt): a walk at its failing qualifier step, a generic at its Name.
    // The site is read from the use's published lookup (R7-N1 e3, F's ruling N1-13 B1, PLAN G1298): a generic whose walk fails at a
    // qualifier records there too, as its plain twin (g2 as x5, g1 as x5t, g3 at `Missing`; before, each at its Name `G.Missing.P` or
    // `Missing.P`), and a requirement too (r1, r1g; before, r1g at `G.Missing.P`), whose Constraint keeps its record over the failed
    // requirement (one dependent InvalidConstraint_Kd at it, Q13).
    // The resolver's other outcomes keep their own records (C-405 C2): an inherited Contract reached without its qualifier is
    // QualificationRequired_Kd relating the base clause and the declaration (x6; SPEC 9.5, before E2-4b InvalidConstraint_Kd), and an
    // ambiguity is AmbiguousBinding_Kd alone (x7; its dependent InvalidConstraint_Kd went, M2). The qualified twins are valid.
    [Theory]
    [InlineData("group G\n    contract P\ncontract A: G.P\n", "InvalidConstraint_Kd 3:1 contract|InaccessibleBinding_Kd 3:13 G.P, inaccessible declaration 'P'")] // x1
    [InlineData("group G\n    struct S\ncontract A: G.S\n", "InvalidConstraint_Kd 3:1 contract|InaccessibleBinding_Kd 3:13 G.S, inaccessible declaration 'S'")] // x1s
    [InlineData("group G\n    public contract P\ncontract A: G.P\n", "")] // x1t
    [InlineData("contract A: Nope\n", "InvalidConstraint_Kd 1:1 contract|UnresolvedBinding_Kd 1:13 Nope")] // x2
    [InlineData("group G\n    public contract Q\ncontract A: G.Missing.P\n", "InvalidConstraint_Kd 3:1 contract|UnresolvedBinding_Kd 3:13 G.Missing")] // x5
    [InlineData("group G\n    public contract Q\nfunc f(x: ref/G.Missing.P) -> () => ()\n", "UnresolvedBinding_Kd 3:15 G.Missing")] // x5t
    [InlineData("contract A: Nope<i32>\n", "InvalidConstraint_Kd 1:1 contract|UnresolvedBinding_Kd 1:13 Nope")] // x2g
    [InlineData("func f(x: ref/Nope<i32>) -> () => ()\n", "UnresolvedBinding_Kd 1:15 Nope")] // x2gt
    [InlineData("group G\n    public contract Q\ncontract A: G.Missing.P<i32>\n", "InvalidConstraint_Kd 3:1 contract|UnresolvedBinding_Kd 3:13 G.Missing")] // g2
    [InlineData("group G\n    public contract P<T>\ncontract A: G.P<i32>\n", "")] // g2t
    [InlineData("group G\n    public contract Q\nfunc f(x: ref/G.Missing.P<i32>) -> () => ()\n", "UnresolvedBinding_Kd 3:15 G.Missing")] // g1
    [InlineData("func f(x: ref/Missing.P<i32>) -> () => ()\n", "UnresolvedBinding_Kd 1:15 Missing")] // g3
    [InlineData("group G\n    public contract Q\nstruct S<T>\n    T is G.Missing.P\n", "InvalidConstraint_Kd 3:1 struct|UnresolvedBinding_Kd 4:10 G.Missing|InvalidConstraint_Kd 4:10 G.Missing.P")] // r1
    [InlineData("group G\n    public contract Q\nstruct S<T>\n    T is G.Missing.P<i32>\n", "InvalidConstraint_Kd 3:1 struct|UnresolvedBinding_Kd 4:10 G.Missing|InvalidConstraint_Kd 4:10 G.Missing.P<i32>")] // r1g
    [InlineData("open struct B\n    public contract P\n    public init() => ()\nstruct D: B\n    contract A: P\n    public init(): base() => ()\n", "InvalidConstraint_Kd 5:5 contract|QualificationRequired_Kd 5:17 P, base clause of D, inherited declaration")] // x6
    [InlineData("open struct B\n    public contract P\n    public init() => ()\nstruct D: B\n    contract A: B.P\n    public init(): base() => ()\n", "")] // x6t
    [InlineData("alias G1\nalias G2\ngroup G1\n    public contract P\ngroup G2\n    public contract P\ncontract A: P\n", "InvalidConstraint_Kd 7:1 contract|AmbiguousBinding_Kd 7:13 P")] // x7
    [InlineData("alias G1\nalias G2\ngroup G1\n    public contract P\ngroup G2\n    public contract P\ncontract A: G1.P\n", "")] // x7t
    public void AParentNoLookupSelectsIsTheResolversMiss(string source, string records)
        => Assert.Equal(records, string.Join('|', DiagnosticCorpus.LocatedRecords(source + "public func main() => ()\n")));

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
    [InlineData("contract C: PrimitiveInteger, Nope", "InvalidConstraint_Kd UnresolvedBinding_Kd")] // Nope: the resolver's miss (SPEC 9.5; PLAN G1207)
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

    // SPEC 8.3.2, 8.6.4, 9.6: a member call gathers the requirements of every Contract the premises supply for its receiver, a clause row
    // at depth included (d1: C2's at height 4); one requirement reached by two paths is one (8.6.1), a second same-named requirement is
    // ambiguous (r11, accepted before R7-C2c2 c2-3a read Enumerate's rows), and height 3 has no C2 row (d1n).
    [Theory]
    [InlineData(TreeC2 + C2 + "func f<T>(v: ref/" + Height4 + ") -> i32\n    T is C2\n    return v.name()", "")]
    [InlineData(TreeC2 + "contract Other\n    func name(self: ref/Self) -> i32\n" + C2 + "func f<T>(v: ref/" + Height4 + ") -> i32\n    T is C2\n    " + Height4 + " is Other\n    return v.name()", "AmbiguousBinding_Kd")]
    [InlineData(TreeC2 + C2 + "func f<T>(v: ref/" + Height4 + ") -> i32\n    T is C2\n    " + Height4 + " is Named\n    return v.name()", "")]
    [InlineData(TreeC2 + C2 + "func f<T>(v: ref/T.(Tree).Child.(Tree).Child.(Tree).Child) -> i32\n    T is C2\n    return v.name()", "UnresolvedBinding_Kd")]
    public void AMemberCallGathersTheRequirementsOfEveryRowAtDepth(string source, string codes) => AssertBindingCodes(source, codes);

    // SPEC 8.3.2, 8.6.4, 15.6: a Contract a clause row proves at depth gives its members (the Middle's R7-C2a a1 consent, R1; an
    // UnresolvedBinding_Kd before R7-C2c2 c2-3a read Enumerate's rows): a member call (f-member-v), one through a stored reference
    // (f-store-v) and a depth-3 chain (r-l3) are accepted; a write while the call's Loan is live is the Loan's conflict (f-member-n).
    // r-l3's `cap<T>` erases a Closure formed in it, whose instantiation holds the inherited `T` (SPEC 3.2.1, 6.1.3), so the erasure
    // needs `T is Owned` beside the capture's row at depth (7.7.4, 15.2.3; PLAN G856, the owner's O-249 Q4 (a)); with `T is CL1` alone,
    // as c2-3a wrote it, the erasure is the Constraint failure (r-l3-unowned).
    [Theory]
    [InlineData(PeekTree + PeekProbe + "    let r = v.peek()\n    return r\n" + PeekMain, "")]
    [InlineData(PeekTree + PeekProbe + "    let r = v.peek()\n    v = b\n    return r\n" + PeekMain, "ComparisonLoanConflict_Kd")]
    [InlineData(PeekTree + PeekStore + PeekMain, "")]
    [InlineData(DepthThreeChain + " and Owned" + DepthThreeCap, "")]
    [InlineData(DepthThreeChain + DepthThreeCap, "UnprovenConstraint_Kd")]
    public void AConformanceProvenAtDepthGivesItsMembers(string source, string codes)
    {
        var c = MinimalEmissionTest.Check(source);
        Assert.Equal(codes, string.Join(" ", TestDiagnostics.Of(c).Select(static x => x.Code)));
    }

    // PLAN G949 S1-b1 d4 (DECISIONS BU-220): an edit that removes a conformance keeps no identity or path of it after the next pass;
    // the overlay drops what the pass did not register at the end of the Bind (before: the identity stayed, unregistered and named).
    [Fact]
    public void AnEditThatRemovesAConformanceKeepsNoIdentityOrPathOfIt()
    {
        var c = Sources("Self is C");
        var (s, contract) = (Container(c, "S").SymbolOf()!, Container(c, "C").SymbolOf()!);
        var impls = Impls(c);
        Assert.NotNull(impls.Find(s, contract));
        ReplaceSelfClause(c, "Self is E");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Null(impls.Find(s, contract));
        Assert.DoesNotContain(impls.Paths, x => ReferenceEquals(x.Contract, contract));
        Assert.DoesNotContain(impls.Identities, x => ReferenceEquals(x.Contract, contract));
    }

    // PLAN G949 S1-r2 (s1r1-design 1.4; review-s1r2 C1): ImplTable's pass view lists only the stores of the pass's modules. Begun again
    // at the overlay's own value (the run's, so no row goes stale) with the source modules alone, it lists the program's paths,
    // identities and Type lists and no library one; at the pass's last registrar step the composed lists are that view, then the base's
    // rows, each a library Type's, and they equal the composed lists after the Bind. PLAN G949 S1-1 (B6; ruling 203 (3b)): no pass
    // begins the frozen base again (its Begin clears the base's admission points, a write to the frozen layer), so the base's rows are
    // read through a program pass's composed lists, not a library-only view.
    [Fact]
    public void ThePassViewListsOnlyThePassModulesStores()
    {
        var c = Sources("Self is C");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var impls = Impls(c);
        var (paths, identities) = (impls.Paths.ToList(), impls.Identities.ToList());
        impls.Begin(impls.Pass, new(c.SourceModules, null, new()));
        Assert.NotEmpty(impls.InPass.Paths);
        Assert.All(impls.InPass.Paths, static x => Assert.False(Library(x.Type)));
        Assert.All(impls.InPass.Identities, static x => Assert.False(Library(x.Type)));
        Assert.All(impls.InPass.ByType, static x => Assert.All(x, static y => Assert.False(Library(y.Type))));
        var (programPaths, programIdentities) = (impls.InPass.Paths.ToList(), impls.InPass.Identities.ToList());
        impls.Advance(ImplTable.AdmissionPoint.AssociatedBindings);
        var (libraryPaths, libraryIdentities) = (impls.Paths.Skip(programPaths.Count).ToList(), impls.Identities.Skip(programIdentities.Count).ToList());
        Assert.NotEmpty(libraryPaths);
        Assert.All(libraryPaths, static x => Assert.True(Library(x.Type)));
        Assert.All(libraryIdentities, static x => Assert.True(Library(x.Type)));
        Assert.Equal(paths, [.. programPaths, .. libraryPaths]);
        Assert.Equal(identities, [.. programIdentities, .. libraryIdentities]);

        static bool Library(BindingSymbol type) => type.Declaration.Kotonoha.Syntax.IsLibrary;
    }

    // PLAN G949 S1-b1 d8: after an edit that removes one conformance and adds another, Identities lists the pass's registrations in
    // their order, the program's first (before: the removed identity kept its place and the added one followed every other).
    [Fact]
    public void IdentitiesAfterAnEditFollowTheRegistrationOrder()
    {
        var c = Sources("Self is C");
        ReplaceSelfClause(c, "Self is E");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(["S: E", "T: D"], Impls(c).Identities.Where(static x => !x.Type.Declaration.Kotonoha.Syntax.IsLibrary).Select(static x => x.Type.Name + ": " + x.Contract.Name));
    }

    // PLAN G949 S1-b1 c15 (the Front's RR3, review C4): Paths lists the program's paths in registration order, declared, conditional
    // and inherited, then the library's; the base interleaved them (program declared, library declared, program conditional, library
    // conditional, program inherited, library inherited), and this row fails there on that order alone. The order is internal: the
    // program completes with no record, its obligations are the library's alone as without its conformances, and EffectObligations'
    // rows, which read Paths, are the program's in their order and the library's as without the program's conformances: only where the
    // program's rows stand among the library's follows Paths.
    [Fact]
    public void PathsListTheProgramsPathsInRegistrationOrderThenTheLibrarys()
    {
        const string Contract = "contract C\n    func get(self: ref/Self) -> i32\n        effect confined\n";
        var c = CompilationTestHelper.ParseSuccess(Contract + "open struct B\n    Self is C\n    public init() => ()\n    public func get(self: ref/Self) -> i32 => 1\n" +
            "struct H : B\n    public init() => ()\n");
        c.Kotonoha.AddSource(new SourceDocument("G.kimi", "struct G<T>\n    public init() => ()\n    Self is C when T is Copy\n        public func get(self: ref/Self) -> i32 => 2\n"));
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Analyze().IsVerified);
        var library = CompilationTestHelper.ParseSuccess(Contract);
        Assert.True(library.Bind().IsComplete, Describe(library));
        Assert.Equal(library.Binding.Obligations.Select(static x => x.ToString()), c.Binding.Obligations.Select(static x => x.ToString()));
        var rows = EffectRows(c);
        Assert.Equal(["B: Self is C get", "G: Self is C get", "H: B get"], rows.Where(static x => !x.Library).Select(static x => x.Row));
        Assert.NotEmpty(EffectRows(library));
        Assert.Equal(EffectRows(library).Select(static x => x.Row), rows.Where(static x => x.Library).Select(static x => x.Row));
        var paths = Impls(c).Paths.ToList();
        var first = paths.FindIndex(static x => x.Type.Declaration.Kotonoha.Syntax.IsLibrary);
        Assert.Equal(["B: C", "G: C", "H: C"], paths.Take(first).Select(static x => x.Type.Name + ": " + x.Contract.Name));
        Assert.DoesNotContain(paths.Skip(first), static x => !x.Type.Declaration.Kotonoha.Syntax.IsLibrary);
    }

    // PLAN G949 S1-r1b (c15's extension, F's ruling 65; s1r1-design 8.8): EffectObligations reads three per-pass sources in order:
    // ImplTable's paths, the virtual declarations and the selected uses of bounded Callable clauses, each list Binding's own part's
    // entries, then LibraryLayer.Part's (S1-b2's commits 3a-3c). From the split the library pass fills the layer's part and the program
    // pass Binding's own; the library declares no `virtual` and no bounded Callable clause, so the layer's three lists (the virtual
    // declarations, the uses and the clauses) are empty and Binding's own part holds every row: the program's obligations stand in the
    // three sources' order, and AvailableCallableEffects finds apply's bound in the program part's clause. Fails on a head that hands
    // the program pass the layer's lists or composes them first. Valid: each confined body is confined (SPEC 8.11.2-8.11.4).
    [Fact]
    public void TheEffectSourcesAreTheProgramPartsInTheirOrder()
    {
        const string Contract = "contract C\n    func get(self: ref/Self) -> i32\n        effect confined\n";
        var c = CompilationTestHelper.ParseSuccess(Contract + "struct S\n    Self is C\n    public init() => ()\n    public func get(self: ref/Self) -> i32 => 1\n" +
            "open struct Base\n    public init() => ()\n    public virtual func score(self: objref/Self) -> i32\n        effect confined\n        return 1\n" +
            "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n        effect confined\n    return f()\n" +
            "func entry() -> i32\n    let f = func [] () -> i32 => 12\n    return apply(f@ref)\n");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.True(c.Analyze().IsVerified);
        Assert.Equal(["S: Self is C get", "Base: public virtual func score(self: objref/Self) -> i32 score", ": f@ref entry"], EffectRows(c).Where(static x => !x.Library).Select(static x => x.Row));
        var (layer, own) = (c.Library.Layer.Part, (Binding.PassPart)typeof(Binding).GetField("programPart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!);
        Assert.Equal((0, 0, 0), (layer.VirtualDeclarations.Count, layer.CallableEffectUses.Count, layer.CallableEffectClauses.Count));
        Assert.Equal("score", Assert.Single(own.VirtualDeclarations).Name);
        var clause = Assert.Single(own.CallableEffectClauses);
        var use = Assert.Single(own.CallableEffectUses);
        Assert.Equal("f@ref", use.At.ToString());
        Assert.Same(clause, use.Clause);
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), static x => x.ValueCallOf() is not null);
        var plan = call.ValueCallOf()!;
        var evidence = new List<EffectEvidence>();
        Assert.True(c.Binding.AvailableCallableEffects(plan.CalleeType.Components[0], plan.DeclaredSignature, plan.ReceiverKind, c.Binding.ConstraintScope(call), evidence));
        Assert.Same(clause, Assert.Single(evidence).Clause);
    }

    // PLAN G949 S1-b1 (the Front's RR2): a path the sweep removed is taken from the pool for the next new key, emptied of the
    // satellites and witnesses of its earlier conformance, and a warm rebind afterwards allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ANewPathReusesARemovedPathEmptiedAndAWarmRebindAllocatesNothing()
    {
        var c = Sources("Self is C");
        var (s, d, e) = (Container(c, "S").SymbolOf()!, Container(c, "D").SymbolOf()!, Container(c, "E").SymbolOf()!);
        var removed = Impls(c).Paths.Single(x => ReferenceEquals(x.Type, s));
        Assert.NotEmpty(removed.FunctionWitnesses!);
        ReplaceSelfClause(c, "Self is D");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.DoesNotContain(Impls(c).Paths, x => ReferenceEquals(x, removed));
        ReplaceSelfClause(c, "Self is E");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var reused = Impls(c).Paths.Single(x => ReferenceEquals(x.Type, s));
        Assert.Same(removed, reused);
        Assert.Same(e, reused.Contract);
        Assert.All(reused.FunctionWitnesses!, x => Assert.Same(e, x.Requirement.Contract));
        Assert.All(reused.WitnessScopes!, x => Assert.Same(e, x.Requirement.Contract));
        Assert.Same(e, Assert.Single(reused.Witnesses).Identity.Contract);
        Assert.DoesNotContain(Impls(c).Paths, x => ReferenceEquals(x.Type, s) && ReferenceEquals(x.Contract, d));
        c.Binding.Bind(BindingMode.Final);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
    }

    // PLAN G949 S1-b1 (the Front's RR2, review C2): Reuse, then ResetPass as the first access in a pass runs it, leaves a used path and
    // identity no entry and no state of their earlier conformance. Their lists, maps and settable fields are found by reflection, so one
    // added later without its line in Reuse or ResetPass fails here.
    [Fact]
    public void AReusedPathAndIdentityKeepNoEntryOrStateOfTheirEarlierConformance()
    {
        var c = Sources("Self is C");
        var (s, e) = (Container(c, "S").SymbolOf()!, Container(c, "E").SymbolOf()!);
        var path = Impls(c).Paths.Single(x => ReferenceEquals(x.Type, s));
        var (identity, scope) = (path.Identity, path.Scope);
        object?[] values = [path, s, s.Type, path.Declaration];
        Spoil(path, values);
        Spoil(identity, values);
        identity.Reuse(s, e).ResetPass(Impls(c).Pass);
        path.Reuse(s, e, identity).ResetPass(Impls(c).Pass);
        AssertEmptied(path);
        AssertEmptied(identity);
        Assert.Same(e, path.Contract);
        Assert.Same(identity, path.Identity);
        Assert.NotSame(scope, path.Scope);
        Assert.Same(path, path.Scope.ConformancePath);
    }

    // PLAN G949 S1-r1b, rule (a) (F's rulings 169 and 170; the S1-r2 note's v6: R5, R6, R10; SPEC 8.6.1, 8.5.2, 9.6): a program pass reads a
    // library Type's conformances as registered only from its own registrar's step. A Self clause binds before the program's conformance
    // declarations register (pass 0), so a projection over a library Type there finds no associated Type yet; it is an early read judged
    // again at point 1 (VerifyEarlyProjections, G621: a member it finds then rests on its location-only Unsupported_Kd, R5, R6), and a
    // conditional conformance (Range's Iterable when S is PrimitiveInteger) is not registered at point 1 either (R10: no IteratorType).
    // These are today's records; from the split the library pass registers the base before the program pass, and a program pass that read
    // the base from its start would find Array's Element at once (R5 and R6 lose their Unsupported_Kd) and Range's IteratorType at point 1
    // (R10 moves: on the trial split it read three InvalidConstraint_Kd and an AmbiguousBinding_Kd).
    [Theory]
    [InlineData("contract K<T>\nstruct W\n    Self is K<Kimi.Array<i32>.Element>", "InvalidConstraint_Kd Unsupported_Kd")]
    [InlineData("contract K<T>\nstruct V\n    Self is K<i32>\nstruct W\n    Self is K<Kimi.Dictionary<i32, i32>.Element>", "InvalidConstraint_Kd Unsupported_Kd")]
    [InlineData("contract K<T>\nstruct W\n    Self is K<Kimi.Range<i32, i32>.IteratorType>", "InvalidConstraint_Kd UnresolvedBinding_Kd")]
    public void ALibraryConformanceIsReadFromItsRegistrarsStep(string source, string codes) => AssertBindingCodes(source, codes);

    // PLAN G949 S1-r1b, rule (a) at ImplTable's lookups (F's ruling 170 condition (2), and g9 of the S1-r2 note's C7): a reader that does not
    // bind the library reads a library identity with the paths of the registrar steps it reached. OwningIterator's and BorrowingIterator's
    // LendingIterator identity has its direct path from the Self-clause registrar (point 1) and the ancestor path of `Self is Iterator when
    // I is Iterator` from the conditional registrar (point 2): one path at point 1, two at point 2. Range's Iterable is conditional only: not
    // found and not in Range's list at point 1, found with its path at point 2. The reader's step is set on the compilation's overlay after a
    // Bind (the one pass binds the library, so its own reads are every row's).
    [Fact]
    public void ALibraryIdentityReadsThePathsOfTheStepsItsReaderReached()
    {
        var c = CompilationTestHelper.ParseSuccess(string.Empty);
        Assert.True(c.Bind().IsComplete);
        var impls = Impls(c);
        var identities = impls.Identities.ToArray();
        var (owning, lending) = Named("OwningIterator", "LendingIterator");
        var (borrowing, _) = Named("BorrowingIterator", "LendingIterator");
        var (range, iterable) = Named("Range", "Iterable");
        var direct = impls.PathsOf(impls.Find(owning, lending)!)[0];
        var all = (impls.Paths.Count, impls.Identities.Count, impls.ByType.Count);
        typeof(ImplTable).GetField("holdsBase", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(impls, false);
        var counts = new List<(int, int, int)>();
        foreach (var point in Enum.GetValues<ImplTable.AdmissionPoint>())
        {
            // Every step (review-s1r1b Part 2, N1): the composed lists' base parts are their prefixes at the step, and a path is found
            // by its key from its point on.
            typeof(ImplTable).GetField("step", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(impls, point);
            counts.Add((impls.Paths.Count, impls.Identities.Count, impls.ByType.Count));
            Assert.Equal(point != ImplTable.AdmissionPoint.PassStart, impls.TryPath(direct.Type, direct.Contract, direct.Declaration, direct.RootContract) is not null);
            if (point is ImplTable.AdmissionPoint.PassStart or ImplTable.AdmissionPoint.AssociatedBindings)
            {
                Assert.Equal(point == ImplTable.AdmissionPoint.PassStart, impls.Find(owning, lending) is null);
                Assert.Equal(point == ImplTable.AdmissionPoint.PassStart, impls.OfType(range) is not { Count: not 0 });
                continue;
            }

            var first = point == ImplTable.AdmissionPoint.SelfClauses;
            foreach (var adapter in (BindingSymbol[])[owning, borrowing])
            {
                var identity = impls.Find(adapter, lending);
                Assert.NotNull(identity);
                Assert.Equal(first ? 1 : 2, impls.PathsOf(identity).Count);
            }

            var conditional = impls.Find(range, iterable);
            Assert.Equal(first, conditional is null);
            Assert.Equal(first ? 0 : 1, conditional is null ? 0 : impls.PathsOf(conditional).Count);
            Assert.Equal(!first, impls.OfType(range)?.Exists(x => ReferenceEquals(x.Contract, iterable)) == true);
        }

        Assert.Equal((0, 0, 0), counts[0]);
        Assert.Equal(all, counts[^1]);
        Assert.All(counts.Zip(counts.Skip(1)), x => Assert.True(x.First.Item1 <= x.Second.Item1 && x.First.Item2 <= x.Second.Item2 && x.First.Item3 <= x.Second.Item3, string.Join(", ", counts)));
        Assert.True(counts[1].Item1 < counts[2].Item1, $"point 2 adds paths: {string.Join(", ", counts)}");

        (BindingSymbol Type, BindingSymbol Contract) Named(string type, string contract)
            => identities.Where(x => x.Type.Name == type && x.Contract.Name == contract).Select(static x => (x.Type, x.Contract)).Single();
    }

    // ThreeContracts, then S conforming by `clause` and T conforming to D, each in a source of its own; bound.
    private static Compilation Sources(string clause)
    {
        var c = CompilationTestHelper.ParseSuccess(ThreeContracts);
        c.Kotonoha.AddSource(new SourceDocument("S.kimi", "struct S\n    " + clause + "\n    public func get(self: ref/Self) -> i32 => 1"));
        c.Kotonoha.AddSource(new SourceDocument("T.kimi", "struct T\n    Self is D\n    public func get(self: ref/Self) -> i32 => 2"));
        Assert.True(c.Bind().IsComplete, Describe(c));
        return c;
    }

    // Replaces the Contract S's Self clause names by the one `clause` names, an edit.
    private static void ReplaceSelfClause(Compilation c, string clause)
    {
        var target = Assert.Single(Container(c, "S").ConstraintNodes);
        var donor = Assert.Single(CompilationTestHelper.ParseSuccess("struct S\n    " + clause).Kotonoha.RootKoto.NestedContainers.Single().ConstraintNodes).Right;
        ParseTestHelper.Detach(donor, new CodeBlockKoto(donor.CodeContext));
        Assert.True(KotoHelper.Replace(target, target.Right, donor));
    }

    private static ImplTable Impls(Compilation c) => (ImplTable)typeof(Binding).GetField("impls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;

    // EffectObligations' rows of the latest pass in their order: a library row as its subject's reference and definition, a program
    // row as its subject's declaration, the subject's first line and the definition.
    private static List<(bool Library, string Row)> EffectRows(Compilation c)
    {
        var rows = new List<EffectObligation>();
        c.Binding.EffectObligations(rows);
        return [.. rows.Select(x => (x.Subject.IsLibrary, (x.Subject.IsLibrary ? x.Subject.ToString() : Site(c.SyntaxOf(x.Subject))) + " " + x.Definition?.Name))];
    }

    // A program node as its enclosing declaration's name and its own first line.
    private static string Site(Koto node)
    {
        var owner = node.Parent;
        while (owner is not DeclarationContainerKoto)
        {
            owner = owner!.Parent;
        }

        return ((DeclarationContainerKoto)owner).Name + ": " + node.ToString().Split('\n')[0];
    }

    // A conformance identity's or path's state that Reuse and ResetPass empty or reset: each list and map, and each other settable
    // property but the key Reuse sets (Type, Contract, Identity), the pass stamp and the scope Reuse replaces.
    private static IEnumerable<PropertyInfo> PassState(object model)
        => model.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(static x =>
            x.Name is not ("Type" or "Contract" or "Identity" or "Stamp" or "Scope") && (IsCollection(x.PropertyType) || x.GetSetMethod(true) is not null));

    private static bool IsCollection(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() is var definition && (definition == typeof(List<>) || definition == typeof(Dictionary<,>));

    // Gives each list and map of PassState one more entry (a map's key the first of `values` its type accepts, else a default or an
    // uninitialized instance) and each other settable property a value other than its default: true, 1, the first of `values` its type
    // accepts, else an instance of a concrete type.
    private static void Spoil(object model, object?[] values)
    {
        foreach (var property in PassState(model))
        {
            var type = property.PropertyType;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)(property.GetValue(model) ?? Activator.CreateInstance(type)!);
                var entry = type.GetGenericArguments()[0];
                list.Add(entry.IsValueType ? Activator.CreateInstance(entry) : null);
                property.GetSetMethod(true)?.Invoke(model, [list]);
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var map = (IDictionary)(property.GetValue(model) ?? Activator.CreateInstance(type)!);
                var (key, value) = (type.GetGenericArguments()[0], type.GetGenericArguments()[1]);
                map[key.IsValueType ? Activator.CreateInstance(key)! : values.FirstOrDefault(key.IsInstanceOfType) ?? RuntimeHelpers.GetUninitializedObject(key)] =
                    value.IsValueType ? Activator.CreateInstance(value) : null;
                property.GetSetMethod(true)?.Invoke(model, [map]);
            }
            else if (property.GetSetMethod(true) is { } setter)
            {
                var value = type == typeof(bool) ? true : type == typeof(int) ? 1 : type.IsEnum ? Enum.ToObject(type, 1)
                    : values.FirstOrDefault(type.IsInstanceOfType) ?? (type.IsAbstract || type.IsValueType ? null : RuntimeHelpers.GetUninitializedObject(type));
                setter.Invoke(model, [value]);
            }
        }
    }

    // Each PassState property of a model object reads empty, null, false or 0.
    private static void AssertEmptied(object model)
    {
        foreach (var property in PassState(model))
        {
            var value = property.GetValue(model);
            Assert.True(value is null or false or 0 || value is ICollection { Count: 0 } || (value is Enum e && Convert.ToInt64(e) == 0), $"{model.GetType().Name}.{property.Name}: {value}");
        }
    }

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
