// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConditionalMemberBindingTest
{
    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void DirectCallRequiresSubstitutedBlockConditions(string argument, bool valid)
    {
        var c = Parse($"contract C\n    func f() -> i32\nstruct S<T>\n    Self is C when T is Copy\n        public func f() -> i32 => 1\nfunc use() -> i32 => S<{argument}>.f()");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
        if (valid)
        {
            var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
            Assert.Equal("i32", call.BoundCall!.DeclaringType!.Components[0].Name);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyBlockBodiesReceiveTheCondition(bool outside)
    {
        var c = Parse("contract C\nstruct S<T>\n    func copy<U>(x: U) -> U\n        U is Copy\n        return x\n    Self is C when T is Copy\n        public func inside(x: T) -> T => copy(x)\n" + (outside ? "    func outside(x: T) -> T => copy(x)" : string.Empty));
        Assert.True(c.Bind().IsComplete == !outside, Describe(c));
    }

    [Fact]
    public void FunctionAndOuterGenericSlotsAreSubstitutedIndependently()
    {
        var c = Parse("contract C\nstruct S<T>\n    Self is C when T is Copy\n        public func f<U>(x: T, y: U) -> U => y\nfunc use() -> bool => S<i32>.f(1, true)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal("bool", call.BoundCall!.TypeArguments[0].Name);
        Assert.Equal("bool", call.BoundType!.Name);
    }

    [Theory]
    [InlineData("Copy", true)]
    [InlineData("Owned", false)]
    public void SelectedImplementationMustProveAnotherBlocksConditions(string condition, bool valid)
    {
        var c = Parse($"contract A\n    func f()\ncontract B\nstruct S<T>\n    Self is A when T is {condition}\n    Self is B when T is Copy\n        public func f() => ()");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void AssociatedSpecificationsInBlocksAreValidated(string argument, bool valid)
    {
        var c = Parse($"contract C\n    associate E\n    E is Copy\n    func f(x: E) -> E\nstruct S<T>\n    Self is C when T is Copy\n        associate C.E is {argument}\n        public func f(x: {argument}) -> {argument} => x");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlockMembersShareOuterDuplicateRules(bool outside)
    {
        var second = outside ? "    public func f() => ()" : "    Self is B when T is Owned\n        public func f() => ()";
        var c = Parse("contract A\ncontract B\nstruct S<T>\n    Self is A when T is Copy\n        public func f() => ()\n" + second);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Fact]
    public void ComputedAccessorAndWitnessUseTheBlockEnvironment()
    {
        var c = Parse("contract C\n    property item: i32 has get\nstruct S<T>\n    func check<U>() -> i32\n        U is Copy\n        return 1\n    Self is C when T is Copy\n        public computed item: i32\n            get(self: ref/Self) -> i32 => check<T>()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void EnumAllowsConditionalFunctions()
    {
        var c = Parse("contract C\n    func f()\nenum S<T>\n    None\n    Self is C when T is Copy\n        public func f() => ()\nfunc use() => S<i32>.f()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("var field: i32")]
    [InlineData("struct Nested")]
    [InlineData("Self is Copy")]
    [InlineData("Self is C when T is Copy")]
    [InlineData("init() => ()")]
    [InlineData("deinit => ()")]
    public void BlocksRejectLayoutOrNestedConformanceDeclarations(string member)
    {
        var c = Parse($"contract C\nstruct S<T>\n    Self is C when T is Copy\n        {member}", allowDiagnostics: true);
        Assert.True(c.Kotonoha.DiagnosticCollection.GetArray().Length != 0 || !c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void InstanceCallKeepsReceiverTypeAndOriginSubstitution(string argument, bool valid)
    {
        var c = Parse($"contract C\n    func f(self: ref/Self) -> i32\nstruct S<T>\n    Self is C when T is Copy\n        public func f(self: ref/Self) -> i32 => 1\nfunc use(x: ref/S<{argument}>) -> i32 => x.f()");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
        if (valid)
        {
            var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
            Assert.NotNull(call.BoundCall!.Receiver);
            Assert.NotNull(call.BoundCall.InputOrigins[0]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalSignaturesUseOnlyTheirDeclarationEnvironment(bool outside)
    {
        var members = "    Self is C when T is A\n        public func f(x: Box<T>) => ()\n";
        var c = Parse("contract A\ncontract C\nstruct Box<T>\n    T is A\nstruct S<T>\n" + members + (outside ? "    func outside(x: Box<T>) => ()" : string.Empty));
        Assert.True(c.Bind().IsComplete == !outside, Describe(c));
    }

    [Theory]
    [InlineData("T is A, T.A.E is Copy")]
    [InlineData("T.A.E is Copy, T is A")]
    public void AssociatedPremisesPrecedeDependentBlockSignatures(string conditions)
    {
        var c = Parse($"contract A\n    associate E\ncontract C\nstruct Box<T>\n    T is Copy\nstruct S<T>\n    Self is C when {conditions}\n        public func f(x: Box<T.A.E>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("Copy", true)]
    [InlineData("Owned", false)]
    public void BlockConditionsApplyToPropertyWitnessesFromOtherBlocks(string condition, bool valid)
    {
        var c = Parse($"contract A\n    property item: i32 has get\ncontract B\nstruct S<T>\n    Self is A when T is {condition}\n    Self is B when T is Copy\n        public computed item: i32\n            get(self: ref/Self) -> i32 => 1");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    [Theory]
    [InlineData("i32", "i32", true)]
    [InlineData("i32", "string", false)]
    public void BlockAssociatedBindingsRemainSeparateAcrossOverlappingPaths(string first, string second, bool valid)
    {
        var c = Parse($"contract A\n    associate E\ncontract B: A\ncontract C: A\nstruct S<T>\n    Self is B when T is Copy\n        associate A.E is {first}\n    Self is C when T is Owned\n        associate A.E is {second}");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    [Fact]
    public void BlockAssociatedBindingsRemainSeparateAcrossDisjointPaths()
    {
        var c = Parse("contract A\n    associate E\ncontract B: A\ncontract C: A\nstruct S<T>\n    Self is B when T is i32\n        associate A.E is i32\n    Self is C when T is string\n        associate A.E is string\nfunc use(x: S<i32>, y: S<string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "use");
        var contract = Walk(c.Kotonoha.RootKoto).OfType<ContractKoto>().Single(x => x.Name == "A").BoundSymbol!;
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(use.Parameters[i].Type.BoundType!, contract, use, out var path));
            Assert.Equal(i == 0 ? "i32" : "string", Assert.Single(path!.AssociatedTypes).Value.Name);
        }
    }

    [Fact]
    public void AssociatedSpecificationCannotTargetAnUnrelatedContract()
    {
        var c = Parse("contract A\n    associate E\ncontract B\nstruct S<T>\n    Self is B when T is Copy\n        associate A.E is i32");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void AssociatedSpecificationCannotPublishItsBindingOutsideTheBlock()
    {
        var c = Parse("contract C\n    associate E\nstruct S<T>\n    Self is C when T is Copy\n        associate C.E is i32\n        public func inside(x: Self.C.E) => ()\n    func outside(x: Self.C.E) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.ToString().Contains("Self.C.E", StringComparison.Ordinal));
    }

    [Fact]
    public void ConstraintErrorsCannotBeHiddenByAnotherApplicableCandidate()
    {
        var c = Parse("contract C\nstruct Bad\n    Self is C\nstruct S<T>\n    Self is C when T is Copy\n        public func f<U>(x: U) -> i32\n            U is Missing\n            return 1\n    public func f(x: i32) -> i32 => 2\nfunc use() -> i32 => S<i32>.f(1)");
        Assert.False(c.Bind().IsComplete);
        var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Null(call.BoundCall);
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, call) && x.Code == DiagnosticCode.InvalidConstraint_Kd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownConditionCannotCommitAnAlternative(bool proven)
    {
        var c = Parse("contract C\nstruct S<T>\n    Self is C when T is Copy\n        public func f(x: i32) -> i32 => 1\n    public func f<U>(x: U) -> i32 => 2\nfunc use<T>(x: S<T>) -> i32\n" + (proven ? "    T is Copy\n" : string.Empty) + "    return S<T>.f(1)");
        Assert.True(c.Bind().IsComplete == proven, Describe(c));
        var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        if (proven)
        {
            Assert.Empty(((FunctionKoto)call.BoundCall!.Target.Declaration).GenericArguments);
        }
        else
        {
            Assert.Null(call.BoundCall);
            Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, call) && x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        }
    }

    [Fact]
    public void RefutedCandidateAllowsAnApplicableMemberOfTheCommittedGroup()
    {
        var c = Parse("contract C\nstruct S<T>\n    Self is C when T is Copy\n        public func f(x: i32) -> i32 => 1\n    public func f<U>(x: U) -> i32 => 2\nfunc use() -> i32 => S<string>.f(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Single(((FunctionKoto)call.BoundCall!.Target.Declaration).GenericArguments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionStrengthDoesNotBreakAnOrdinaryOverloadTie(bool reversed)
    {
        const string Copy = "    Self is A when T is Copy\n        public func f(x: i32) -> i32 => 1\n";
        const string Owned = "    Self is B when T is Owned\n        public func f(x: u32) -> i32 => 2\n";
        var c = Parse("contract A\ncontract B\nstruct S<T>\n" + (reversed ? Owned + Copy : Copy + Owned) + "func use() -> i32 => S<i32>.f(1)");
        Assert.False(c.Bind().IsComplete);
        var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Null(call.BoundCall);
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, call) && x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Fact]
    public void InapplicableGroupDoesNotReopenOuterLookup()
    {
        var c = Parse("contract C\ngroup G\n    func f(x: i32) -> i32 => 1\n    struct S<T>\n        Self is C when T is Copy\n            func f(x: i32) -> i32 => 2\n        func use() -> i32 => f(1)");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall);
    }

    [Theory]
    [InlineData("f", "public func f() -> i32 => 1")]
    [InlineData("item", "public computed item: i32\n            get(self: ref/Self) -> i32 => 1")]
    public void NonCallUsesCheckTheSameConditionAndKeepUnsupportedOperationsPending(string name, string member)
    {
        var c = Parse($"contract C\nstruct S<T>\n    Self is C when T is Copy\n        {member}\nfunc use(x: ref/S<string>) => x.{name}");
        Assert.False(c.Bind().IsComplete);
        var access = Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single(x => x.Right.ToString() == name);
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, access) && x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("computed item: i32\n            get(self: ref/Self) -> i32 => 1")]
    public void EnumBlocksRejectCasesAndComputedMembers(string member)
    {
        var c = Parse($"contract C\nenum S<T>\n    None\n    Self is C when T is Copy\n        {member}", allowDiagnostics: true);
        Assert.True(c.Kotonoha.DiagnosticCollection.GetArray().Length != 0 || !c.Bind().IsComplete);
    }

    [Fact]
    public void InapplicableMemberDoesNotReopenBaseLookup()
    {
        var c = Parse("contract C\nopen struct Base\n    public func f() -> i32 => 1\nstruct S<T>: Base\n    Self is C when T is Copy\n        public func f() -> i32 => 2\nfunc use() -> i32 => S<string>.f()");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall);
    }

    [Fact]
    public void AssociatedBlockSignatureCanUseItsOwnBinding()
    {
        var c = Parse("contract C\n    associate E\n    func f(x: E) -> E\nstruct S<T>\n    Self is C when T is Copy\n        associate C.E is i32\n        public func f(x: Self.C.E) -> Self.C.E => x\nfunc use() -> i32 => S<i32>.f(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void AdditionalBlockAssociatedRequirementsAreProved(string binding, bool valid)
    {
        var c = Parse($"contract C\n    associate E\nstruct S<T>\n    Self is C when T is Copy\n        associate C.E is {binding} and Copy");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OuterAssociatedPremisesAreCollectedBeforeBlockConditions(bool reversed)
    {
        const string Contracts = "contract A\n    associate E\n    E is Copy\ncontract C\nstruct Box<T>\n    T is Copy\n";
        const string Type = "struct S<T>\n    T is A\n    Self is C when T.A.E is Copy\n        public func f(x: Box<T.A.E>) => ()\n";
        var c = Parse(reversed ? Type + Contracts : Contracts + Type);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void InheritedConditionalMemberUsesItsDeclaringBaseArguments(string argument, bool valid)
    {
        var c = Parse($"contract C\nopen struct Base<T>\n    Self is C when T is Copy\n        public func f(x: T) -> T => x\nstruct S<U>: Base<{argument}>\nfunc use(x: {argument}) -> {argument} => S<bool>.f(x)");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
        if (valid)
        {
            var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
            Assert.Equal("Base", call.BoundCall!.DeclaringType!.Symbol!.Name);
            Assert.Equal(argument, call.BoundCall.DeclaringType.Components[0].Name);
        }
    }

    [Fact]
    public void RegisteringTheBlockDoesNotAssumeItsConformance()
    {
        var c = Parse("contract A\n    func f()\nstruct S<T>\n    func demand<U>()\n        U is A\n        ()\n    Self is A when T is Copy\n        public func unrelated() => demand<Self>()");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall);
    }

    [Theory]
    [InlineData("condition")]
    [InlineData("member")]
    [InlineData("associated")]
    public void RebindingInvalidatesBlockMetadataAndCallPlans(string change)
    {
        const string Source = "contract C\n    associate E\n    func f() -> i32\nstruct S<T>\n    Self is C when T is Copy\n        associate C.E is i32\n        public func f() -> i32 => 1\nfunc use() -> i32 => S<i32>.f()";
        var c = Parse(Source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var fragment = Parse(Source.Replace("T is Copy", "T is string", StringComparison.Ordinal).Replace("associate C.E is i32", "associate C.E is Missing", StringComparison.Ordinal).Replace("public func f", "private func f", StringComparison.Ordinal));
        Koto Select(Compilation compilation) => change switch
        {
            "condition" => Walk(compilation.Kotonoha.RootKoto).OfType<IsKoto>().Single(x => x.Left.ToString() == "T").Right,
            "associated" => Walk(compilation.Kotonoha.RootKoto).OfType<IsKoto>().Single(x => x.IsAssociatedConstraint).Right,
            _ => Walk(compilation.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f" && !x.IsRequirement),
        };
        var original = Select(c);
        var replacement = Select(fragment);
        var parent = original.Parent!;
        var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var retained = call.BoundCall!;
        Assert.True(KotoHelper.Replace(parent, original, replacement));
        Assert.False(c.Binding.Bind(BindingMode.Final).IsComplete);
        if (change != "associated")
        {
            Assert.Null(call.BoundCall);
        }

        Assert.True(KotoHelper.Replace(parent, replacement, original));
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        Assert.Same(retained, call.BoundCall);
    }

    [Fact]
    public void ConditionalBlockWritingAndSerializationPreserveTreeAndBinding()
    {
        const string Source = "contract C\n    associate E\n    func f() -> E\nstruct S<T>\n    Self is C when T is Copy\n        associate C.E is i32\n        public func f() -> i32 => 1\n    public func outside() => ()\nfunc use() -> i32 => S<i32>.f()";
        var c = Parse(Source);
        var written = Write(c.Kotonoha);
        var reparsed = Parse(written);
        Assert.Equal(written, Write(reparsed.Kotonoha));
        Assert.True(reparsed.Bind().IsComplete, Describe(reparsed));
        var restored = Tinyhand.TinyhandSerializer.Deserialize<Kotonoha>(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha))!;
        restored.OnDeserialized(c);
        Assert.Equal(written, Write(restored));
        foreach (var node in Walk(restored.RootKoto))
        {
            foreach (var child in node.ChildNodes)
            {
                Assert.Same(node, child.Parent);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(512)]
    public void WarmBindingReusesBlocksAndCandidateResultsWithoutAllocations(int count)
    {
        var source = new System.Text.StringBuilder("contract A\n    associate E\n    func f(x: i32) -> i32\ncontract B: A\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("struct S").Append(i).Append("<T>\n    Self is B when T is Copy\n        associate A.E is i32\n        public func f(x: i32) -> i32 => x\n        public func f<U>(x: U) -> i32 => 2\nfunc use").Append(i).Append("() -> i32 => S").Append(i).Append("<i32>.f(S").Append(i).Append("<i32>.f(1))\n");
        }

        var c = Parse(source.ToString());
        Assert.True(c.Bind().IsComplete, Describe(c));
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Equal(0, allocated);
    }

    private static Compilation Parse(string source, bool allowDiagnostics = false)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        if (!allowDiagnostics)
        {
            Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        }

        return c;
    }

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));

    private static string Write(Kotonoha tree)
    {
        var builder = default(IndentedStringBuilder);
        try
        {
            tree.RootKoto.UnparseAll(ref builder);
            return builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }
}
