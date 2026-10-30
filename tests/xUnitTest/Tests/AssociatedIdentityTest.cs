// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class AssociatedIdentityTest(ITestOutputHelper output)
{
    private const string CyclicBaseA = "open struct BaseA\n    Self is HasItem\n    associate HasItem.Item is BaseA.Item\n";
    private const string CyclicBaseG = "open struct BaseG<T>\n    Self is HasItem\n    associate HasItem.Item is BaseG<T>.Item\n    var t: T\n";
    private const string ConditionalInit = "struct HC<S>\n    S is Addable<S>\n    public var x: S.Output\n    public var y: S\n    public init(a: S)\n        S is PrimitiveInteger\n";

    [Theory]
    [InlineData("C<i32>, C<i64>")]
    [InlineData("C<i64>, C<i32>")]
    public void DistinctBoundParentsKeepBothAssociatedTypes(string parents)
    {
        var c = MinimalEmissionTest.Analyze(Declarations(parents));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var q = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single(static x => x.Name == "Q");
        Assert.Equal(2, q.SymbolOf()!.Contract!.AssociatedTypes.Count);
    }

    [Theory]
    [InlineData("Forward", "C<i32>, C<i64>")]
    [InlineData("Reverse", "C<i64>, C<i32>")]
    public void BothFamiliesExecuteThroughTheRefiningContract(string name, string parents)
    {
        const string Use = """

            func relay<T>(source: ref/T, small: ref/i32 during a, large: ref/i64 during a) -> T.(C<i64>).Item(a)
                T is Q
                let first: T.(C<i32>).Item(a) = source.borrow(small)
                return source.borrow(large)
            let source = S.init()
            let small: i32 = 7
            let large: i64 = 9
            require relay(source@ref, small@ref, large@ref) == 9 else => $abort("family")
            Console.writeLine("families")
            """;
        ScalarEmissionTest.EmitFixture("AssociatedIdentity" + name, Declarations(parents) + Use, "families\n");
    }

    [Theory]
    [InlineData("C<i32>, C<i64>")]
    [InlineData("C<i64>, C<i32>")]
    public void AnUnqualifiedFamilyIsAmbiguousAcrossDistinctBindings(string parents)
    {
        var c = MinimalEmissionTest.Analyze("contract C<E>\n    associate Item(a)\ncontract Q: " + parents + "\nfunc use<T>(value: ref/T during a) -> T.Item(a)\n    T is Q\n    $abort(\"unused\")");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Theory]
    [InlineData("i32", 1)]
    [InlineData("i64", 2)]
    public void SubstitutionDeduplicatesOnlyEqualFamilyReferences(string argument, int count)
    {
        var source = "contract C<E>\n    associate Item(a) is E\ncontract Q<U>: C<i32>, C<U>\nstruct S\n    Self is Q<" + argument + ">\n    public init() => ()\nlet value = S.init()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var s = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single(static x => x.Name == "S");
        var reference = Assert.Single(s.ConstraintNodes).ConstraintOf()!.Contract!;
        Assert.Equal(count, reference.Contract!.AssociatedTypes.Count);
        Assert.Equal(count, c.Binding.GetConformance(s.TypeOf()!, reference)!.AssociatedTypes.Count);
    }

    [Theory]
    [InlineData("C<i32>", true)]
    [InlineData("C<i64>", true)]
    [InlineData("C", false)]
    [InlineData("C<i16>", false)]
    public void RefinementsSelectAnExactAvailableReference(string qualifier, bool valid)
    {
        var source = "contract C<E>\n    associate Item(a) for ref/E during a\ncontract Q: C<i32>, C<i64>\n    associate " + qualifier + ".Item(b) is ref/i32 during b\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        if (qualifier == "C")
        {
            Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
        }
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("i64", false)]
    public void UniversalFamilyFactsDoNotCrossBoundReferences(string projected, bool valid)
    {
        var source = "contract C<E>\n    associate Item(a)\ncontract Q: C<i32>, C<i64>\n    associate C<i32>.Item(a) is Copy\n" +
            "func copy<T>(value: T.(C<" + projected + ">).Item(a), marker: ref/T during a)\n    T is Q\n    let duplicate = value\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(valid == c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("U", true)]
    [InlineData("V", false)]
    public void FormationDomainsUseTheSelectedDeclaringReference(string target, bool valid)
    {
        var source = "contract C<E>\n    associate Item(a) for ref/E during a\ncontract Q<U, V>: C<U>, C<V>\n" +
            "func use<T, U, V>(source: ref/T, value: ref/U during a) -> T.(C<" + target + ">).Item(a)\n    T is Q<U, V>\n    $abort(\"unused\")\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("i32", 1)]
    [InlineData("i64", 2)]
    public void InheritedAssociatedIdentitiesComposeBaseSubstitution(string argument, int count)
    {
        var source = "contract C<E>\n    associate Item(a) is E\ncontract Q<U>: C<i32>, C<U>\nopen struct Base<U>\n    Self is Q<U>\n    public init() => ()\n" +
            "struct Derived: Base<" + argument + ">\n    Self is Q<" + argument + ">\n    public init() : base() => ()\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var derived = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single(static x => x.Name == "Derived");
        var reference = Assert.Single(derived.ConstraintNodes).ConstraintOf()!.Contract!;
        Assert.All(c.Binding.GetConformanceDefinition(derived.TypeOf()!, reference)!.Paths, path =>
        {
            Assert.True(path.IsVerified);
            Assert.Equal(count, path.AssociatedTypes.Count);
            foreach (var (identity, type) in path.AssociatedTypes)
            {
                Assert.Same(identity.Contract.Type!.Components[0], type);
            }
        });
    }

    [Fact]
    public void EqualBoundDiamondsNeedOnlyOneSpecification()
    {
        const string Source = "contract C<E>\n    associate Item(a)\ncontract Left: C<i32>\ncontract Right: C<i32>\ncontract Q: Left, Right\nstruct S\n    Self is Q\n    associate Item(a) is i32\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var q = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single(static x => x.Name == "Q");
        Assert.Single(q.SymbolOf()!.Contract!.AssociatedTypes);
    }

    [Fact]
    public void EditsRemoveObsoleteAssociatedMappings()
    {
        const string Source = "contract C<E>\n    associate Item(a) is E\ncontract Q: C<i32>, C<i64>\nstruct S\n    Self is Q\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var q = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single(static x => x.Name == "Q");
        var s = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single();
        var mapping = c.Binding.GetConformance(s.TypeOf()!, q.SymbolOf()!)!;
        var old = mapping.AssociatedTypes.Keys.ToArray();
        Assert.Equal(2, old.Length);
        var donor = CompilationTestHelper.ParseSuccess("contract Q: C<i64>").Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single();
        Assert.True(KotoHelper.Replace(q, q.Bases[0], ParseTestHelper.Detach(donor.Bases[0], new CodeBlockKoto(q.CodeContext))));
        Assert.False(mapping.IsVerified);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(old[1], Assert.Single(mapping.AssociatedTypes).Key);
        Assert.False(mapping.AssociatedTypes.ContainsKey(old[0]));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBoundAssociatedRebindDoesNotAllocate()
    {
        var c = MinimalEmissionTest.Analyze(Declarations("C<i32>, C<i64>"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var bytes = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void AmbiguityExplainsBothBoundReferencesInCliAndLsp()
    {
        const string Source = "contract C<E>\n    associate Item(a)\ncontract Q: C<i32>, C<i64>\n    associate C.Item(a) is i32\nlet present = 1";
        var path = Path.GetFullPath("associated-identity.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal("AmbiguousBinding_Kd", record.Code);
        Assert.Equal("no unique best candidate among 2 candidates", record.Label);
        Assert.Equal(new SourceSpan(Source.IndexOf("C.Item", StringComparison.Ordinal), "C.Item".Length), record.Span);
        Assert.Equal(["C<i32>.Item", "C<i64>.Item"], record.Related!.Select(static x => x.Label));
        Assert.Contains("distinct associated-Type identities", record.Note);
        Assert.Empty(record.Repairs ?? []);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range!.Value, sent.Range);
            Assert.Equal(record.Code, sent.Code);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void AnAmbiguousSubjectKeepsIndependentRequirementAndExpressionFailures()
    {
        const string Source = "contract C<E>\n    associate Item(a)\ncontract Q: C<i32>, C<i64>\n    associate C.Item(a) is Missing\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        // A requirement name may denote either a Type or a Contract, so a missing one is UnresolvedBinding.
        Assert.Equal(["AmbiguousBinding_Kd", "UnresolvedBinding_Kd", "TypeMismatch_Kd"], TestDiagnostics.Of(c).Select(static x => x.Code));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AmbiguousBindingsCountDistinctReferencesAndReuseFailureStorage()
    {
        const string Source = "contract C<E>\n    associate Item(a)\ncontract Q: C<i32>, C<i64>, C<i16>\n    associate C.Item(a) is i32\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        var bytes = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.Equal(0, bytes);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal("no unique best candidate among 3 candidates", Assert.Single(TestDiagnostics.Of(c)).Label);
    }

    [Fact]
    public void InheritedFamiliesExecuteWithTheirOriginalMappings()
    {
        var declarations = Declarations("C<i32>, C<i64>").Replace("struct S", "open struct S", StringComparison.Ordinal);
        const string Use = """

            struct Derived: S
                public init() : base() => ()
            func relay<T>(source: ref/T, small: ref/i32 during a, large: ref/i64 during a) -> T.(C<i64>).Item(a)
                T is Q
                let first: T.(C<i32>).Item(a) = source.borrow(small)
                return source.borrow(large)
            let source = Derived.init()
            let small: i32 = 7
            let large: i64 = 9
            require relay(source@ref, small@ref, large@ref) == 9 else => $abort("inherited family")
            Console.writeLine("inherited")
            """;
        ScalarEmissionTest.EmitFixture("AssociatedIdentityInherited", declarations + Use, "inherited\n");
    }

    // SPEC 8.3.3 Recursion, Unbounded Types (DECISIONS BU-50): a conformance binding revisited while it is being resolved, directly
    // (`A.Item is A.Item`), through another binding (A and B) or with a growing argument (`G<T> { Item is G<G<T>>.Item }`, A<T> and B<T>),
    // names no Type. Each specification on the cycle and its projection keep their InvalidConstraint_Kd records, as each use of the
    // conformance (a parameter's Type, a call's) and the call's NoApplicableOverload_Kd do, and normalizing or storing an instance whose
    // Field projects through it ends; the growing forms overflowed the stack before (ContractTypeCore at the call, KeptType in the MIR).
    // A Field whose projection names no Type does not substitute (null), so the instance's storage is Rejected, a dependent of the
    // specification's record (SPEC 23.1.2, DECISIONS BU-38): the analysis, and on sel the collector, add no record (before, sel's `check`
    // added CheckFaulted_Kd, "Layout has no row for the Type Item").
    [Theory]
    [InlineData("struct A\n    Self is HasItem\n    associate HasItem.Item is A.Item\n", "A", "A.Item")]
    [InlineData("struct A\n    Self is HasItem\n    associate HasItem.Item is B.Item\nstruct B\n    Self is HasItem\n    associate HasItem.Item is A.Item\n", "A", "B.Item,A.Item")]
    [InlineData("struct G<T>\n    Self is HasItem\n    associate HasItem.Item is G<G<T>>.Item\n", "G<i32>", "G<G<T>>.Item")]
    [InlineData("struct A<T>\n    Self is HasItem\n    associate HasItem.Item is B<A<T>>.Item\nstruct B<T>\n    Self is HasItem\n    associate HasItem.Item is A<T>.Item\n", "A<i32>", "B<A<T>>.Item,A<T>.Item")]
    public void ABindingThatUsesItselfNamesNoType(string declarations, string subject, string specifications)
    {
        var source = "contract HasItem\n    associate Item\nstruct HA<S>\n    S is HasItem\n    var x: S.Item\n" +
            "    public init(x: S.Item) => self.x = x@move\n" + declarations + $"func f(h: HA<{subject}>) => ()\npublic func main() -> ()\n" +
            $"    var h = HA<{subject}>.init(null)\n    return\n";
        var c = MinimalEmissionTest.Analyze(source);
        var use = $"InvalidConstraint_Kd HA<{subject}>";
        Assert.Equal(
            [.. specifications.Split(',').SelectMany(static x => (string[])[$"InvalidConstraint_Kd associate HasItem.Item is {x}", $"InvalidConstraint_Kd {x}"]),
                use, use, $"NoApplicableOverload_Kd HA<{subject}>.init(null)", use],
            c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}"));
        Assert.Empty(c.Problems);
        Assert.Empty(c.Instances?.Records ?? []);
        var holder = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f").Parameters[0].Type.TypeOf()!;
        Assert.Equal((Binding.StorageOutcome.Rejected, null), (c.Binding.PrepareTypeStorage(holder), c.Types.StoredParts(holder, out _)[0]));
        Assert.Empty(c.BuildMir().Findings);
    }

    // SPEC 8.3.3, 8.6.1 (DECISIONS BU-50): an inherited conformance binds what its base's binds, so where the base's binding is rejected
    // (`BaseA.Item is BaseA.Item`) a derived Type's projection names no Type either, one level down (inhcyc), two levels down or under a
    // generic base's arguments: the base's specification and projection, each derived declaration and base clause and the use keep their
    // records, and the Field's storage is Rejected, a dependent (before, the inherited arm reported no rejection, so the Field kept
    // `DerA.Item`, which sel's `check` faulted on: CheckFaulted_Kd, "Layout has no row for the Type Item"). The twin whose base binds `i32`
    // is valid, its Field i32.
    [Theory]
    [InlineData(CyclicBaseA + "struct DerA : BaseA\n    var m: i32\n", "DerA", "BaseA.Item", "DerA:BaseA")]
    [InlineData(CyclicBaseA + "open struct DerA : BaseA\n    var m: i32\nstruct DerB : DerA\n    var n: i32\n", "DerB", "BaseA.Item", "DerA:BaseA,DerB:DerA")]
    [InlineData(CyclicBaseG + "struct DerG : BaseG<i32>\n    var m: i32\n", "DerG", "BaseG<T>.Item", "DerG:BaseG<i32>")]
    [InlineData("open struct BaseA\n    Self is HasItem\n    associate HasItem.Item is i32\nstruct DerA : BaseA\n    var m: i32\n", "DerA", "", "")]
    public void AnInheritedBindingKeepsItsBasesVerdict(string declarations, string subject, string specification, string derivations)
    {
        var c = MinimalEmissionTest.Analyze("contract HasItem\n    associate Item\nstruct HA<S>\n    S is HasItem\n    var x: S.Item\n" + declarations +
            $"func f(h: HA<{subject}>) => ()\npublic func main() -> ()\n    return\n");
        var use = $"InvalidConstraint_Kd HA<{subject}>";
        var derived = derivations.Split(':', ',');
        string[] records = specification.Length == 0 ? [] :
            [$"InvalidConstraint_Kd associate HasItem.Item is {specification}", $"InvalidConstraint_Kd {specification}",
                .. derived.Chunk(2).SelectMany(static x => (string[])[$"InvalidTypeFormation_Kd struct {x[0]}", $"InvalidConstraint_Kd {x[1]}"]), use, use];
        Assert.Equal(records, c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}"));
        Assert.Empty(c.Problems);
        Assert.Empty(c.Instances?.Records ?? []);
        var holder = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f").Parameters[0].Type.TypeOf()!;
        var verdict = records.Length == 0 ? (Binding.StorageOutcome.Prepared, BoundType.I32) : (Binding.StorageOutcome.Rejected, null);
        Assert.Equal(verdict, (c.Binding.PrepareTypeStorage(holder), c.Types.StoredParts(holder, out _)[0]));
        Assert.Empty(c.BuildMir().Findings);
    }

    // SPEC 8.2.3, 8.7.4 (PLAN G281): a body reads a stored part in its own scope, as a call's signature is read, so the use's premise
    // completes a projection the generic declaration's storage keeps pending (DECISIONS BU-50): `h.x` of `HAd<T>` and `HAd<Wrapping<T>>`
    // (also through `ref/`) and `HNg<Wrapping<T>>` under `T is PrimitiveInteger`, a Case payload constructed (`.A(a + a)`, `.A(s@move)`)
    // and bound by a Pattern, a Field written, and `T.(HasItem).Item is i32`'s premise, also as a conditional constructor's `self.x`
    // (SPEC 7.5: written, read, under the identity premise); closed instances read as before. Before, each was TypeMismatch_Kd ("found
    // T.Output", "expected S.Output"). Read without the premise (`T is Addable<T>`), or a read, write, payload, Pattern binding or
    // constructor write as bool, stays TypeMismatch_Kd.
    [Theory]
    [InlineData("func f<T>(a: T) -> T\n    T is PrimitiveInteger\n    let h = HAd<T>.init(x: a + a)\n    return h.x\n", "let v = f(3@i32)", "")]
    [InlineData("func f<T>(a: Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    let h = HAd<Wrapping<T>>.init(x: a + a)\n    return h.x\n", "let v = f(3@Wrapping<i32>)", "")]
    [InlineData("func f<T>(a: Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    let h = HNg<Wrapping<T>>.init(x: -a)\n    return h.x\n", "let v = f(3@Wrapping<u8>)", "")]
    [InlineData("func f<T>(h: ref/HAd<Wrapping<T>>) -> Wrapping<T>\n    T is PrimitiveInteger\n    return h.x\n", "let h = HAd<Wrapping<i32>>.init(x: 3@Wrapping<i32>)\n    let v = f(h@ref)", "")]
    [InlineData("func f<T>(a: Wrapping<T>) -> EW<Wrapping<T>>\n    T is PrimitiveInteger\n    let e = EW<Wrapping<T>>.A(a + a)\n    return e@move\n", "let v = f(3@Wrapping<i32>)", "")]
    [InlineData("func f<T>(a: Wrapping<T>) -> EW<Wrapping<T>>\n    T is PrimitiveInteger\n    let s = a + a\n    let e: EW<Wrapping<T>> = .A(s@move)\n    return e@move\n", "let v = f(3@Wrapping<i32>)", "")]
    [InlineData("func f<T>(e: EW<Wrapping<T>>, a: Wrapping<T>) -> Wrapping<T>\n    T is PrimitiveInteger\n    return match e@move\n        .A(let v) => v\n        .B => a\n", "let v = f(EW<Wrapping<i32>>.A(3@Wrapping<i32>), 1@Wrapping<i32>)", "")]
    [InlineData("func f<T>(h: uniq/HAd<T>, a: T) -> ()\n    T is PrimitiveInteger\n    h.x = a + a\n    return\n", "var h = HAd<i32>.init(x: 1)\n    f(h@uniq, 2)", "")]
    [InlineData("func f<T>(h: ref/HP<T>) -> i32\n    T is HasItem\n    T.(HasItem).Item is i32\n    return h.x\n", "let h = HP<SrcI>.init(x: 3)\n    let v = f(h@ref)", "")]
    [InlineData(ConditionalInit + "        self.x = a + a\n        self.y = a\n", "let h = HC<i32>.init(a: 3)", "")]
    [InlineData(ConditionalInit + "        self.x = a + a\n        self.y = self.x + a\n", "let h = HC<i32>.init(a: 3)", "")]
    [InlineData("struct HQ<S>\n    S is HasItem\n    public var x: S.Item\n    public init(n: i32)\n        S.(HasItem).Item is i32\n        self.x = n\n", "let h = HQ<SrcI>.init(n: 3)", "")]
    [InlineData("func f(a: i32, w: Wrapping<i32>) -> (i32, Wrapping<i32>)\n    let h = HAd<i32>.init(x: a + a)\n    let k = HAd<Wrapping<i32>>.init(x: w + w)\n    return (h.x, k.x)\n", "let v = f(3, 4@Wrapping<i32>)", "")]
    [InlineData("func f<T>(a: T) -> bool\n    T is PrimitiveInteger\n    let h = HAd<T>.init(x: a + a)\n    return h.x\n", "let v = f(3@i32)", "return h.x")]
    [InlineData("func f<T>(h: ref/HAd<T>) -> T\n    T is Addable<T>\n    return h.x\n", "let h = HAd<i32>.init(x: 3)\n    let v = f(h@ref)", "return h.x")]
    [InlineData("func f<T>(h: uniq/HAd<T>, a: T) -> ()\n    T is PrimitiveInteger\n    h.x = true\n    return\n", "var h = HAd<i32>.init(x: 1)\n    f(h@uniq, 2)", "h.x = true")]
    [InlineData("func f<T>(a: T) -> EW<T>\n    T is PrimitiveInteger\n    return EW<T>.A(true)\n", "let v = f(3@i32)", "EW<T>.A(true)")]
    [InlineData("func f<T>(e: EW<T>, a: T) -> bool\n    T is PrimitiveInteger\n    return match e@move\n        .A(let v) => v\n        .B => true\n", "let v = f(EW<i32>.A(3), 1)", "match e@move")]
    [InlineData(ConditionalInit + "        self.x = true\n        self.y = a\n", "let h = HC<i32>.init(a: 3)", "self.x = true")]
    public void AStoredPartIsReadInItsUseScope(string declarations, string use, string mismatch)
    {
        const string Holders = """
            contract HasItem
                associate Item
            struct SrcI
                Self is HasItem
                associate HasItem.Item is i32
            struct HP<S>
                S is HasItem
                public var x: S.Item
                public init(x: S.Item) => self.x = x@move
            struct HAd<S>
                S is Addable<S>
                public var x: S.Output
                public init(x: S.Output) => self.x = x@move
            struct HNg<S>
                S is Negatable
                public var x: S.Output
                public init(x: S.Output) => self.x = x@move
            enum EW<S>
                S is Addable<S>
                A(S.Output)
                B

            """;
        var c = MinimalEmissionTest.Analyze(Holders + declarations + $"public func main() -> ()\n    {use}\n    return\n");
        Assert.Equal(mismatch.Length == 0 ? [] : [$"TypeMismatch_Kd {mismatch}"], c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}".Split('\n')[0]));
        Assert.Empty(c.Problems);
    }

    private static string Declarations(string parents) => """
        contract C<E>
            associate Item(a)
            func borrow(self: ref/Self, value: ref/E during a) -> Self.Item(a)
        contract Q: PARENTS
        struct S
            Self is Q
            associate C<i32>.Item(a) is ref/i32 during a
            associate C<i64>.Item(a) is ref/i64 during a
            public init() => ()
            public func borrow(self: ref/Self, value: ref/i32 during a) -> ref/i32 during a => value
            public func borrow(self: ref/Self, value: ref/i64 during a) -> ref/i64 during a => value

        """.Replace("PARENTS", parents, StringComparison.Ordinal);
}
