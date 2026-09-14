// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class InheritedReceiverBindingTest
{
    [Theory]
    [InlineData("private", true)]
    [InlineData("public", false)]
    public void OnlyAnAccessibleLayerCommitsLookup(string access, bool valid)
    {
        var c = Parse($"open struct Base\n    public func f(x: i32) -> i32 => x\nstruct D: Base\n    {access} func f(x: string) -> i32 => 1\nfunc use() -> i32 => D.f(1)");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
        if (valid)
        {
            Assert.Equal("Base", Call(c).BoundCall!.DeclaringType!.Name);
        }
    }

    [Theory]
    [InlineData("value")]
    [InlineData("type")]
    public void ReceiverMismatchDoesNotReopenTheBaseLayer(string syntax)
    {
        var c = Parse("open struct Base\n    public func f(self: ref/Self) -> i32 => 1\nstruct D: Base\n    public func f(x: i32) -> i32 => x\nfunc use(x: ref/D) -> i32 => " + (syntax == "value" ? "x.f()" : "D.f(x)"));
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Call(c).BoundCall);
        Assert.False(c.Binding.TryGetReceiverOperation(Call(c), out _));
    }

    [Fact]
    public void ATypeDeclarationDoesNotStopValueMemberLookup()
    {
        var c = Parse("open struct Base\n    public func f() -> i32 => 1\nstruct D<f>: Base\nfunc use() -> i32 => D<i32>.f()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal("Base", Call(c).BoundCall!.DeclaringType!.Name);
    }

    [Fact]
    public void StorageProjectionSubstitutesOriginsAtEveryBaseLayer()
    {
        var c = Parse("open struct Base<T>\n    public let view: T\nopen struct Middle<U>: Base<U>\nstruct D<V>: Middle<V>\nfunc use origin a(x: ref/D<ref/i32 from a>) -> ref/i32 from a => x.view");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var access = Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single();
        Assert.True(c.Binding.TryGetReceiverOperation(access, out var plan));
        Assert.NotNull(plan.BasePath!.Parent);
        Assert.Same(access.BoundType!.Origin, plan.BasePath.Type.Components[0].Origin);
        Assert.Same(access.BoundType.Origin, plan.BasePath.Parent!.Type.Components[0].Origin);
        Assert.Equal("i32", access.BoundType.Components[0].Name);
    }

    [Theory]
    [InlineData("x.f(1)", 1)]
    [InlineData("S.f(1, x)", 2)]
    public void ReceiverPositionIsIndependentOfSourceEvaluationOrder(string expression, int explicitCount)
    {
        var c = Parse($"struct S\n    public func f(value: i32, self: ref/Self) -> i32 => value\nfunc use(x: ref/S) -> i32 => {expression}");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var plan = Call(c).BoundCall!;
        Assert.Equal(1, plan.Target.ReceiverIndex);
        Assert.Equal(explicitCount, plan.ArgumentOperations.Length);
        Assert.Equal(explicitCount == 1, plan.Receiver is not null);
        if (plan.Receiver is not null)
        {
            Assert.Same(plan.Receiver, plan.ReceiverOperation.Source);
            Assert.Equal(1, plan.ReceiverOperation.ParameterIndex);
            Assert.Equal(0, plan.ArgumentToParameter[0]);
        }
        else
        {
            Assert.Equal(1, plan.ArgumentToParameter[1]);
        }
    }

    [Fact]
    public void AGroupParameterNamedSelfIsAnOrdinaryArgument()
    {
        var c = Parse("group G\n    public func f(self: i32) -> i32 => self\nfunc use() -> i32 => G.f(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(-1, Call(c).BoundCall!.Target.ReceiverIndex);
    }

    [Fact]
    public void AnUnboundCallDoesNotGainMemberProjection()
    {
        var c = Parse("open struct Base\n    public func f(self: ref/Self) -> i32 => 1\nstruct D: Base\nfunc use(x: ref/D) -> i32 => D.f(x)");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Call(c).BoundCall);
        Assert.False(c.Binding.TryGetReceiverOperation(Call(c), out _));
    }

    [Theory]
    [InlineData("ref", "ref", ArgumentAdaptation.SameSemanticsReborrow)]
    [InlineData("uniq", "uniq", ArgumentAdaptation.SameSemanticsReborrow)]
    [InlineData("uniq", "ref", ArgumentAdaptation.CrossSemanticsBorrow)]
    public void ProjectedCallsRetainTheirPlanButRequireEffectProof(string input, string expected, ArgumentAdaptation quality)
    {
        var c = Parse($"open struct Base<T>\n    public func f(self: {expected}/Self) -> i32 => 1\nopen struct Middle<U>: Base<U>\nstruct D: Middle<i32>\nfunc use(x: {input}/D) -> i32 => x.f()");
        Assert.False(c.Bind().IsComplete);
        var call = Call(c);
        Assert.Null(call.BoundCall);
        Assert.True(c.Binding.TryGetReceiverOperation(call, out var plan));
        Assert.Equal(ArgumentOperationKind.BaseBorrow, plan.Kind);
        Assert.Equal(quality, plan.Adaptation);
        Assert.Equal(ConstraintProof.Unknown, plan.ObjectCompatibility);
        Assert.NotNull(plan.BasePath!.Parent);
        Assert.Equal("Base", plan.BasePath.Type.Symbol!.Name);
        Assert.Equal("i32", plan.BasePath.Type.Components[0].Name);
        Assert.Same(((MemberAccessKoto)call.Method).Left, plan.Source);
        Assert.Same(plan.SourceType!.Origin, plan.ParameterType!.Origin);
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, call) && x.Code == DiagnosticCode.UnprovenConstraint_Kd);
    }

    [Fact]
    public void SharedReceiversCannotSupplyExclusiveBaseAccess()
    {
        var c = Parse("open struct Base\n    public func f(self: uniq/Self) => ()\nstruct D: Base\nfunc use(x: ref/D) => x.f()");
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.TryGetReceiverOperation(Call(c), out _));
    }

    [Fact]
    public void StandardInheritedStorageDoesNotBorrowTheWholeBase()
    {
        var c = Parse("open struct Base<T>\n    public var item: T\nstruct D: Base<i32>\nfunc use(x: ref/D) -> i32 => x.item");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var access = Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single();
        Assert.True(c.Binding.TryGetReceiverOperation(access, out var plan));
        Assert.Equal(ArgumentOperationKind.StorageProjection, plan.Kind);
        Assert.Equal(ConstraintProof.Proven, plan.ObjectCompatibility);
        Assert.Equal("i32", access.BoundType!.Name);
    }

    [Theory]
    [InlineData("ref/D", true)]
    [InlineData("ref/Base", false)]
    [InlineData("ref/Sibling", false)]
    public void ProtectedAccessUsesTheOriginalReceiver(string receiver, bool valid)
    {
        var c = Parse($"open struct Base\n    protected var item: i32\nstruct Sibling: Base\nstruct D: Base\n    func use(x: {receiver}) -> i32 => x.item");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    [Fact]
    public void AccessorFailureCannotFallBackToAnotherProperty()
    {
        var c = Parse("open struct Base\n    public var item: i32\nstruct D: Base\n    public var item: i32\n        private get\nfunc use(x: ref/D) -> i32 => x.item");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InaccessibleBinding_Kd);
    }

    [Theory]
    [InlineData("ref", "ref", ArgumentAdaptation.Exact)]
    [InlineData("uniq", "uniq", ArgumentAdaptation.SameSemanticsReborrow)]
    [InlineData("uniq", "ref", ArgumentAdaptation.CrossSemanticsBorrow)]
    public void DirectReceiverCallsRetainRequiredBorrowOperations(string input, string expected, ArgumentAdaptation quality)
    {
        var c = Parse($"struct S\n    public func f(self: {expected}/Self) -> i32 => 1\nfunc use(x: {input}/S) -> i32 => x.f()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(quality, Call(c).BoundCall!.ReceiverOperation.Adaptation);
    }

    [Fact]
    public void OwningPlacesMayBeBorrowedForADirectReceiver()
    {
        var c = Parse("struct S\n    public func f(self: ref/Self) -> i32 => 1\nfunc use(x: S) -> i32 => x.f()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(ArgumentOperationKind.Borrow, Call(c).BoundCall!.ReceiverOperation.Kind);
    }

    [Fact]
    public void AdaptationAdvantagesAcrossSourceArgumentsAreIncomparable()
    {
        var c = Parse("struct S\n    public func f(self: uniq/Self, x: ref/i32) -> i32 => 1\n    public func f(self: ref/Self, x: uniq/i32) -> i32 => 2\nfunc use(s: uniq/S, x: uniq/i32) -> i32 => s.f(x)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => ReferenceEquals(x.Node, Call(c)) && x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Fact]
    public void ReceiverAdaptationPrecedesGenericPreference()
    {
        var c = Parse("struct S\n    public func f<U>(self: uniq/Self, x: U) -> i32 => 1\n    public func f(self: ref/Self, x: i32) -> i32 => 2\nfunc use(s: uniq/S, x: i32) -> i32 => s.f(x)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Single(Call(c).BoundCall!.TypeArguments.ToArray());
    }

    [Fact]
    public void InheritedTypeFunctionWitnessPreservesBaseArguments()
    {
        var c = Parse("struct Box<T>\ncontract C\n    associate E\n    func f(x: E) -> E\nopen struct Base<T>\n    public func f(x: Box<T>) -> Box<T> => x\nstruct D<U>: Base<U>\n    Self is C\n    associate C.E is Box<U>");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var witness = Assert.Single(Path(c, "D", "C").Witnesses);
        Assert.Equal("Base", witness.Function!.DeclaringType.Symbol!.Name);
        Assert.NotNull(witness.Function.BasePath);
        Assert.Equal(ConstraintProof.Proven, witness.Function.ObjectCompatibility);
    }

    [Fact]
    public void InheritedBorrowedWitnessIsUnavailableUntilItsEffectsAreProven()
    {
        var c = Parse("contract C\n    func f(self: ref/Self) -> i32\nopen struct Base\n    public func f(self: ref/Self) -> i32 => 1\nstruct D: Base\n    Self is C");
        Assert.False(c.Bind().IsComplete);
        var path = Path(c, "D", "C");
        var witness = Assert.Single(path.Witnesses);
        Assert.False(path.IsVerified);
        Assert.Null(path.GetImplementation(witness.Requirement));
        Assert.Equal("D", witness.Function!.RequirementReceiver!.Components[0].Name);
        Assert.Equal("Base", witness.Function.ImplementationReceiver!.Components[0].Name);
        Assert.Equal(ConstraintProof.Unknown, witness.Function.ObjectCompatibility);
    }

    [Theory]
    [InlineData("self: Self", "i32", "1")]
    [InlineData("self: ref/Self", "Self", "missing")]
    [InlineData("self: ref/Self, other: Self", "i32", "1")]
    public void ReceiverProjectionDoesNotRewriteOwnersResultsOrOtherArguments(string parameters, string result, string expression)
    {
        var c = Parse($"contract C\n    func f({parameters}) -> {result}\nopen struct Base\n    public func f({parameters}) -> {result} => {expression}\nstruct D: Base\n    Self is C");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code is DiagnosticCode.MissingContractImplementation_Kd or DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Theory]
    [InlineData("self: i32")]
    [InlineData("self: ref/ref/Self")]
    [InlineData("self: unsafe/Self")]
    public void InvalidReceiverDeclarationsCannotSupplyCalls(string parameter)
    {
        var c = Parse($"struct S\n    func f({parameter}) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTypeFormation_Kd);
    }

    [Fact]
    public void UnqualifiedInstanceCallsCannotPretendToBeUnboundCalls()
    {
        var c = Parse("struct S\n    func f(self: ref/Self) => ()\n    func use(x: ref/Self) => f(x)");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Call(c).BoundCall);
    }

    [Fact]
    public void APropertyStillCommitsTheValueRoleWhenACallWasWritten()
    {
        var c = Parse("open struct Base\n    public func f() -> i32 => 1\nstruct D: Base\n    public var f: i32\nfunc use(x: ref/D) -> i32 => x.f()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NotCallable_Kd);
    }

    [Fact]
    public void CallArgumentsCannotDisambiguateTypeAndValuePaths()
    {
        var c = Parse("struct S\n    public func f(x: i32) -> i32 => x\nstruct Other\n    public func f(self: ref/Self, x: string) -> i32 => 1\nfunc use(S: ref/Other) -> i32 => S.f(1)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateOrderDoesNotChangeReceiverRanking(bool reversed)
    {
        const string Shared = "    public func f(self: ref/Self) -> i32 => 1\n";
        const string Exclusive = "    public func f(self: uniq/Self) -> i32 => 2\n";
        var c = Parse("struct S\n" + (reversed ? Exclusive + Shared : Shared + Exclusive) + "func use(x: uniq/S) -> i32 => x.f()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(SemanticsKind.Uniq, Call(c).BoundCall!.ReceiverOperation.ParameterType!.Semantics);
    }

    [Fact]
    public void NamedArgumentsAreComparedBySourcePosition()
    {
        var c = Parse("struct S\n    public func f(self: ref/Self, a: i32, b: uniq/i32) -> i32 => 1\n    public func f(self: ref/Self, b: ref/i32, a: i32) -> i32 => 2\nfunc use(s: ref/S, x: uniq/i32) -> i32 => s.f(b: x, a: 1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(2, Call(c).BoundCall!.ArgumentToParameter[0]);
        Assert.Equal(ArgumentAdaptation.SameSemanticsReborrow, Call(c).BoundCall!.ArgumentOperations[0].Adaptation);
    }

    [Fact]
    public void FewerUsedDefaultsWinsOnlyAfterEquivalentEarlierJudgments()
    {
        var c = Parse("struct S\n    public func f(self: ref/Self, x: i32, y: i32 = 0) -> i32 => 1\n    public func f(x: i32, self: ref/Self) -> i32 => 2\nfunc use(s: ref/S) -> i32 => s.f(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(1, Call(c).BoundCall!.Target.ReceiverIndex);
    }

    [Fact]
    public void ExpectedResultsFilterCandidatesWithoutAdaptingTheirResults()
    {
        var c = Parse("struct S\n    public func f(self: ref/Self, x: i32) -> i32 => 1\n    public func f(self: ref/Self, x: i64) -> bool => true\nfunc use(s: ref/S) -> bool => s.f(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal("bool", Call(c).BoundCall!.ReturnType.Name);
    }

    [Fact]
    public void ASelectedProjectedMethodDoesNotRetryAfterItsEffectProofFails()
    {
        var c = Parse("open struct Base\n    public func f<U>(self: uniq/Self, x: U) -> i32 => 1\n    public func f(self: ref/Self, x: i32) -> i32 => 2\nstruct D: Base\nfunc use(s: uniq/D, x: i32) -> i32 => s.f(x)");
        Assert.False(c.Bind().IsComplete);
        Assert.True(c.Binding.TryGetReceiverOperation(Call(c), out var plan));
        Assert.Equal(SemanticsKind.Uniq, plan.ParameterType!.Semantics);
        Assert.Equal(ConstraintProof.Unknown, plan.ObjectCompatibility);
    }

    [Fact]
    public void ReceiverExpressionsHaveOneSourceAnchorBeforeExplicitArguments()
    {
        var c = Parse("struct S\n    public func f(value: i32, self: ref/Self) -> i32 => value\nfunc receiver(x: ref/S) -> ref/S => x\nfunc argument() -> i32 => 1\nfunc use(x: ref/S) -> i32 => receiver(x).f(argument())");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(x => x.Method is MemberAccessKoto);
        var plan = call.BoundCall!;
        Assert.Same(((MemberAccessKoto)call.Method).Left, plan.ReceiverOperation.Source);
        Assert.IsType<InvocationKoto>(plan.ReceiverOperation.Source);
        Assert.Same(call.ArgumentNodes[0], plan.ArgumentOperations[0].Source);
        Assert.Equal(1, plan.ReceiverOperation.ParameterIndex);
    }

    [Fact]
    public void ProjectedCustomAccessorWitnessKeepsItsUnprovenEffectObligation()
    {
        var c = Parse("contract C\n    property item: i32 has get\nopen struct Base\n    public computed item: i32\n        get(self: ref/Self) -> i32 => 1\nstruct D: Base\n    Self is C");
        Assert.False(c.Bind().IsComplete);
        var path = Path(c, "D", "C");
        var witness = Assert.Single(path.PropertyWitnesses);
        Assert.Equal(PropertyWitnessKind.AccessorCall, witness.Kind);
        Assert.Equal(ConstraintProof.Unknown, witness.ObjectCompatibility);
        Assert.NotNull(witness.BasePath);
        Assert.Null(path.GetPropertyWitness(witness.Requirement.Property.Symbol, PropertyAccessorKind.Get));
    }

    [Fact]
    public void ProjectedAccessorAccessRetainsItsBaseBorrowPlan()
    {
        var c = Parse("open struct Base\n    public computed item: i32\n        get(self: ref/Self) -> i32 => 1\nstruct D: Base\nfunc use(x: ref/D) -> i32 => x.item");
        Assert.False(c.Bind().IsComplete);
        var use = Walk(c.Kotonoha.RootKoto).OfType<MemberAccessKoto>().Single();
        Assert.True(c.Binding.TryGetReceiverOperation(use, out var plan));
        Assert.Equal(ArgumentOperationKind.BaseBorrow, plan.Kind);
        Assert.Equal(ConstraintProof.Unknown, plan.ObjectCompatibility);
        Assert.Same(plan.SourceType!.Origin, plan.ParameterType!.Origin);
    }

    [Theory]
    [InlineData("ref", false)]
    [InlineData("uniq", true)]
    public void StandardStorageProjectionPreservesWritePermissions(string semantics, bool valid)
    {
        var c = Parse($"open struct Base\n    public var item: i32\nstruct D: Base\nfunc use(x: {semantics}/D) => x.item = 1");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
    }

    [Fact]
    public void PublicConformanceCannotSkipAPrivateSelectedImplementation()
    {
        var c = Parse("public contract C\n    func f() -> i32\npublic open struct Base\n    public func f() -> i32 => 1\npublic struct D: Base\n    Self is C\n    private func f() -> i32 => 2");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void ConditionalInheritedWitnessUsesBaseConditionsWithDerivedArguments()
    {
        var c = Parse("contract C\n    func f() -> i32\nopen struct Base<T>\n    Self is C when T is Copy\n        public func f() -> i32 => 1\nstruct D<U>: Base<U>\n    Self is C when U is Copy");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal("Base", Assert.Single(Path(c, "D", "C").Witnesses).Function!.DeclaringType.Symbol!.Name);
    }

    [Fact]
    public void AncestorPathsKeepTheFullInheritedFunctionCorrespondence()
    {
        var c = Parse("contract A\n    func f() -> i32\ncontract C: A\nopen struct Base<T>\n    public func f() -> i32 => 1\nstruct D<U>: Base<U>\n    Self is A when U is Copy\n    Self is C when U is Owned");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var d = Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "D").BoundType!;
        var a = Walk(c.Kotonoha.RootKoto).OfType<ContractKoto>().Single(x => x.Name == "A").BoundSymbol!;
        var paths = c.Binding.GetConformanceDefinition(d, a)!.Paths;
        Assert.Equal(2, paths.Count);
        Assert.All(paths, x => Assert.NotNull(Assert.Single(x.Witnesses).Function!.BasePath));
    }

    [Theory]
    [InlineData("access")]
    [InlineData("receiver")]
    public void RebindInvalidatesAndRestoresCallOperations(string change)
    {
        const string Source = "struct S\n    public func f(self: ref/Self) -> i32 => 1\nfunc use(x: ref/S) -> i32 => x.f()";
        var c = Parse(Source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var fragment = Parse(change == "access" ? Source.Replace("public func", "private func", StringComparison.Ordinal) : Source.Replace("self: ref", "self: uniq", StringComparison.Ordinal));
        var original = Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var replacement = Walk(fragment.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        var parent = original.Parent!;
        var call = Call(c);
        var oldPlan = call.BoundCall;
        Assert.True(KotoHelper.Replace(parent, original, replacement));
        Assert.False(c.Binding.Bind(BindingMode.Final).IsComplete);
        Assert.Null(call.BoundCall);
        Assert.False(c.Binding.TryGetReceiverOperation(call, out _));
        Assert.True(KotoHelper.Replace(parent, replacement, original));
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        Assert.Same(oldPlan, call.BoundCall);
        Assert.Equal(0, call.BoundCall!.Target.ReceiverIndex);
    }

    [Fact]
    public void RebindReplacesBaseTypeSubstitutions()
    {
        const string Source = "open struct Base<T>\n    public func f() -> i32 => 1\nstruct D: Base<i32>\nfunc use() -> i32 => D.f()";
        var c = Parse(Source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var fragment = Parse(Source.Replace("Base<i32>", "Base<bool>", StringComparison.Ordinal));
        var d = Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "D");
        var original = d.Bases[0];
        var replacement = Walk(fragment.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "D").Bases[0];
        Assert.True(KotoHelper.Replace(d, original, replacement));
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        Assert.Equal("bool", Call(c).BoundCall!.DeclaringType!.Components[0].Name);
        Assert.Same(Call(c).BoundCall!.DeclaringType, Call(c).BoundCall!.BasePath!.Type);
        Assert.True(KotoHelper.Replace(d, replacement, original));
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        Assert.Equal("i32", Call(c).BoundCall!.DeclaringType!.Components[0].Name);
        Assert.Same(Call(c).BoundCall!.DeclaringType, Call(c).BoundCall!.BasePath!.Type);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(512)]
    public void WarmBindReusesInheritedWitnessesPathsAndOperations(int count)
    {
        var source = new System.Text.StringBuilder("contract C\n    func f(x: i32) -> i32\n    property item: i32 has get\nopen struct Base<T>\n    public var item: i32\n    public func f(x: i32) -> i32 => x\n    public func f<U>(x: U) -> i32 => 1\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("struct D").Append(i).Append("<T>: Base<T>\n    Self is C when T is Copy\n    public func read(self: ref/Self) -> i32 => self.item\nfunc use").Append(i).Append("(x: ref/D").Append(i).Append("<i32>) -> i32 => D").Append(i).Append("<i32>.f(x.read())\n");
        }

        var c = Parse(source.ToString());
        Assert.True(c.Bind().IsComplete, Describe(c));
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Equal(0, allocated);
    }

    private static BoundConformancePath Path(Compilation c, string type, string contract)
    {
        var t = Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == type).BoundType!;
        var target = Walk(c.Kotonoha.RootKoto).OfType<ContractKoto>().Single(x => x.Name == contract).BoundSymbol!;
        return Assert.Single(c.Binding.GetConformanceDefinition(t, target)!.Paths);
    }

    private static InvocationKoto Call(Compilation c) => Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
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
}
