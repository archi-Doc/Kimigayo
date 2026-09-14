// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class RuntimeTypeTest
{
    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    [InlineData("objref")]
    [InlineData("objuniq")]
    public void ObjectSemanticsUseSharedAccess(string semantics)
    {
        var c = Parse($"open struct Animal\nstruct Dog: Animal\nfunc f(x: {semantics}/Animal) -> bool => x is not Dog");
        AssertBound(c);
        var test = Test(c);
        Assert.True(test.IsNegated);
        var plan = test.BoundRuntimeTest!.Value;
        Assert.True(plan.RequiresSharedAccess);
        Assert.Same(test.Left.BoundType, plan.OperandType);
        Assert.Same(test.Right.BoundType, plan.TargetType);
        Assert.Null(test.BoundConstraint);
        Assert.Same(BoundType.Boolean, test.BoundType);
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Equal(ControlFlowType.Boolean, flow.Nodes[test].ExpressionType);
        Assert.False(flow.Nodes.ContainsKey(test.Right));
        Assert.Empty(flow.PendingBinding);
    }

    [Theory]
    [InlineData("x is Dog", false)]
    [InlineData("x is not Dog", true)]
    public void RoundTripRetainsTypeSyntaxAndNegation(string expression, bool negated)
    {
        var c = Parse("struct Dog\nfunc f(x: objref/Dog) -> bool => " + expression);
        var test = Test(c);
        Assert.True(test.IsRuntimeTest);
        Assert.Equal(negated, test.IsNegated);
        Assert.IsType<IdentifierNameKoto>(test.Right);
        Assert.Equal(expression, test.ToString());
        var roundTrip = Parse("struct Dog\nfunc f(x: objref/Dog) -> bool => " + test);
        Assert.Equal(negated, Test(roundTrip).IsNegated);
        AssertBound(roundTrip);
        var flow = c.AnalyzeControlFlow();
        Assert.DoesNotContain(flow.Issues, x => x.Message.Contains("bool", StringComparison.OrdinalIgnoreCase));
        Assert.False(flow.Nodes.ContainsKey(test.Right));
        Assert.Contains(test, flow.PendingBinding);
    }

    [Fact]
    public void PrefixNotKeepsItsOrdinaryPrecedence()
    {
        var c = Parse("struct Dog\nfunc f(x: objref/Dog) -> bool => not x is Dog");
        var test = Test(c);
        Assert.IsType<NotKoto>(test.Left);
        Assert.False(test.IsNegated);
        Assert.False(c.Bind().IsComplete);
        var grouped = Parse("struct Dog\nfunc f(x: objref/Dog) -> bool => not (x is Dog)");
        AssertBound(grouped);
        Assert.Empty(grouped.AnalyzeControlFlow(grouped.Binding.TypeSystem).Issues);
    }

    [Theory]
    [InlineData("let b = x is Dog\n    return b")]
    [InlineData("let b = if flag => x is Dog else => x is not Dog\n    return b")]
    public void TestsSupplyBooleanInference(string body)
    {
        var c = Parse("struct Dog\nfunc f(x: objref/Dog, flag: bool) -> bool\n    " + body);
        AssertBound(c);
        Assert.Same(BoundType.Boolean, Walk(c.Kotonoha.RootKoto).OfType<FieldKoto>().Single(x => x.NameKoto.IdentifierName == "b").BoundType);
        Assert.Empty(c.AnalyzeControlFlow(c.Binding.TypeSystem).Issues);
    }

    [Theory]
    [InlineData("i32", "0")]
    [InlineData("Dog", "x")]
    [InlineData("ref/Dog", "x")]
    [InlineData("ref/obj/Dog", "x")]
    [InlineData("unsafe/Dog", "x")]
    [InlineData("(i32, i32)", "x")]
    [InlineData("Choice", "x")]
    public void NonObjectOperandsAreRejected(string type, string operand)
    {
        var c = Parse($"struct Dog\nenum Choice\n    A\nfunc f(x: {type}) -> bool => {operand} is Dog");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Test(c).BoundRuntimeTest);
        Assert.Contains(c.Binding.Issues, x => x.Node == Test(c) && x.Code == DiagnosticCode.TypeMismatch_Kd);
    }

    [Theory]
    [InlineData("contract C\nfunc f(x: objref/C) -> bool => x is Dog")]
    [InlineData("func f<T>(x: obj/T) -> bool => x is Dog")]
    public void UnsupportedObjectCoresCannotBeCertified(string source)
    {
        var c = Parse("struct Dog\n" + source);
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Test(c).BoundRuntimeTest);
    }

    [Theory]
    [InlineData("Self")]
    [InlineData("Dog")]
    public void ConcreteSelfResolvesToItsStruct(string target)
    {
        var c = Parse($"struct Dog\n    func f(x: objref/Self) -> bool => x is {target}");
        AssertBound(c);
        Assert.Equal("Dog", Test(c).BoundRuntimeTest!.Value.TargetType.Name);
    }

    [Theory]
    [InlineData("Dog<i32>", true)]
    [InlineData("Dog<T>", false)]
    [InlineData("Dog", false)]
    [InlineData("T", false)]
    public void TargetsRequireExplicitClosedTypeArguments(string target, bool valid)
    {
        var c = Parse($"struct Animal\nstruct Dog<T>\nfunc f<T>(x: objref/Animal) -> bool => x is {target}");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
        Assert.Equal(valid, Test(c).BoundRuntimeTest.HasValue);
        if (target is "Dog<T>" or "T")
        {
            Assert.Contains(c.Binding.Issues, x => x.Node == Test(c) && x.Code == DiagnosticCode.UnsupportedBinding_Kd);
        }
    }

    [Theory]
    [InlineData("enum E\n    A", "E")]
    [InlineData("contract C", "C")]
    [InlineData("", "Missing")]
    public void NonStructOrMissingTargetsAreRejected(string declarations, string target)
    {
        var c = Parse($"struct Dog\n{declarations}\nfunc f(x: objref/Dog) -> bool => x is {target}");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Test(c).BoundRuntimeTest);
    }

    [Theory]
    [InlineData("public", "public", true)]
    [InlineData("private", "public", false)]
    [InlineData("public", "private", false)]
    public void TargetAndTypeArgumentAccessAreChecked(string targetAccess, string argumentAccess, bool valid)
    {
        var c = Parse($"struct Animal\ngroup G\n    {targetAccess} struct Dog<T>\n    {argumentAccess} struct Item\nfunc f(x: objref/Animal) -> bool => x is G.Dog<G.Item>");
        Assert.True(c.Bind().IsComplete == valid, Describe(c));
        Assert.Equal(valid, Test(c).BoundRuntimeTest.HasValue);
    }

    [Fact]
    public void AssociatedProjectionIsNotAQualifiedStructName()
    {
        var c = Parse("struct Dog\ncontract C\n    associate Item\nfunc f<T>(x: objref/Dog) -> bool\n    T is C\n    return x is T.Item");
        Assert.False(c.Bind().IsComplete);
        var test = Test(c);
        Assert.Null(test.BoundRuntimeTest);
        Assert.Equal(BoundTypeKind.AssociatedProjection, test.Right.BoundType!.Kind);
        Assert.Contains(c.Binding.Issues, x => x.Node == test && x.Code == DiagnosticCode.UnsupportedBinding_Kd);
    }

    [Fact]
    public void RebindingUsesTheCurrentAliasAndClearsAnInvalidatedTest()
    {
        const string Source = "alias A\nstruct Animal\ngroup A\n    public struct Dog\ngroup B\n    public struct Dog\nfunc f(x: objref/Animal) -> bool => x is not Dog";
        var c = Parse(Source);
        AssertBound(c);
        var test = Test(c);
        var original = test.BoundRuntimeTest!.Value.TargetType;
        var alias = Walk(c.Kotonoha.RootKoto).OfType<AliasKoto>().Single();
        alias.QualifiedName[0] = "B";
        AssertBound(c);
        Assert.NotSame(original, test.BoundRuntimeTest!.Value.TargetType);
        alias.QualifiedName[0] = "A";
        AssertBound(c);
        Assert.Same(original, test.BoundRuntimeTest!.Value.TargetType);

        var oldTarget = test.Right;
        var fragment = Parse(Source.Replace("is not Dog", "is not Missing", StringComparison.Ordinal));
        var newTarget = Test(fragment).Right;
        Assert.True(KotoHelper.Replace(test, oldTarget, newTarget));
        Assert.False(c.Bind().IsComplete);
        Assert.Null(test.BoundRuntimeTest);
        Assert.Contains(test, c.AnalyzeControlFlow(c.Binding.TypeSystem).PendingBinding);
        Assert.True(KotoHelper.Replace(test, newTarget, oldTarget));
        AssertBound(c);
        Assert.Same(original, test.BoundRuntimeTest!.Value.TargetType);
    }

    [Fact]
    public void AGenericSelfRetainsItsUnresolvedArguments()
    {
        var c = Parse("struct Dog<T>\n    func f(x: objref/Self) -> bool => x is Self");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Test(c).BoundRuntimeTest);
        Assert.Contains(c.Binding.Issues, x => x.Node == Test(c) && x.Code == DiagnosticCode.UnsupportedBinding_Kd);
    }

    [Fact]
    public void SourceOriginsAndUnreachableResultsAreRetained()
    {
        var c = Parse("struct Dog\nfunc f origin a(x: objref/Dog from a) -> bool\n    return true\n    return (x) is not Dog");
        AssertBound(c);
        var test = Test(c);
        Assert.NotNull(test.BoundRuntimeTest!.Value.OperandType.Origin);
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Equal(ControlFlowType.Boolean, flow.Nodes[test].ExpressionType);
        Assert.False(flow.Nodes.ContainsKey(test.Right));
        var invalid = Parse("struct Dog\nfunc f(x: objref/Dog) -> i32\n    return 0\n    return x is Dog");
        Assert.False(invalid.Bind().IsComplete);
    }

    [Theory]
    [InlineData("(return true)")]
    [InlineData("stop()")]
    public void NeverOperandChecksTheTargetWithoutBooleanExits(string operand)
    {
        var c = Parse($"struct Dog\nfunc stop() -> Never => stop()\nfunc f() -> bool\n    return {operand} is not Dog");
        AssertBound(c);
        var test = Test(c);
        Assert.Same(BoundType.Never, test.BoundRuntimeTest!.Value.OperandType);
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.False(flow.Nodes[test].CanCompleteNormally);
        Assert.False(flow.Nodes.ContainsKey(test.Right));
    }

    [Theory]
    [InlineData("Missing", "true")]
    [InlineData("Dog", "1")]
    public void NeverDoesNotBypassTargetOrTransferValidation(string target, string operand)
    {
        var c = Parse($"struct Dog\nfunc f() -> bool => (return {operand}) is {target}");
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("obj/Dog")]
    [InlineData("i32")]
    [InlineData("(Dog, Dog)")]
    [InlineData("Dog from a")]
    public void ForbiddenTargetSyntaxIsRejectedWithoutExpandingTheGrammar(string target)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, $"struct Dog\nfunc f(x: objref/Dog) -> bool => x is {target}");
        Assert.True(c.Kotonoha.DiagnosticCollection.GetArray().Length != 0 || !c.Bind().IsComplete);
    }

    [Fact]
    public void SemanticsSlashRemainsAnOuterOperator()
    {
        var c = Parse("struct Dog\nfunc f(x: objref/Dog) -> bool => x is obj/Dog");
        var test = Test(c);
        Assert.Equal("obj", test.Right.ToString());
        Assert.IsType<SlashKoto>(test.Parent);
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void RootQualifiedTargetUsesTypeLookup()
    {
        var c = Parse("struct Animal\ngroup G\n    public struct Dog\nfunc f(x: objref/Animal) -> bool => x is ::G.Dog");
        AssertBound(c);
        Assert.Equal("Dog", Test(c).BoundRuntimeTest!.Value.TargetType.Name);
    }

    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    public void ShortCircuitConditionsVisitOnlyValueOperands(string operation)
    {
        var c = Parse($"struct Dog\nfunc f(x: objref/Dog, flag: bool) -> bool\n    require x is not Dog {operation} flag else => return false\n    return true");
        AssertBound(c);
        var test = Test(c);
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.Empty(flow.PendingBinding);
        Assert.False(flow.Nodes.ContainsKey(test.Right));
    }

    [Fact]
    public void RequiredCleanupCanStopAValidObjectTypedOperand()
    {
        var c = Parse("struct Dog\nfunc f(x: objref/Dog) -> bool => (work: do\n    defer => loop => ()\n    exit to work: x\n) is Dog");
        AssertBound(c);
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Empty(flow.Issues);
        Assert.False(flow.Nodes[Test(c)].CanCompleteNormally);
    }

    [Fact]
    public void GenericLeadingConstraintContextIsSelectedBeforeLookup()
    {
        const string Source = "struct Dog\nfunc f<T>(x: objref/Dog)\n    x is Dog\n    ()";
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, Source);
        Assert.NotEmpty(c.Kotonoha.DiagnosticCollection.GetArray());
        var clause = Walk(c.Kotonoha.RootKoto).OfType<IsKoto>().Single();
        Assert.False(clause.IsRuntimeTest);
        var ordinary = Parse(Source.Replace("f<T>", "f", StringComparison.Ordinal));
        Assert.True(Test(ordinary).IsRuntimeTest);
        AssertBound(ordinary);
    }

    [Fact]
    public void ConstraintNegationRetainsItsRequirementTree()
    {
        var c = Parse("func f<T>(x: T)\n    T is not Copy\n    ()");
        c.Bind();
        var clause = Walk(c.Kotonoha.RootKoto).OfType<IsKoto>().Single();
        Assert.False(clause.IsRuntimeTest);
        Assert.False(clause.IsNegated);
        Assert.IsType<NotKoto>(clause.Right);
        Assert.NotNull(clause.BoundConstraint);
        Assert.Null(clause.BoundRuntimeTest);
    }

    [Fact]
    public void OwnershipExplicitlyRejectsTheTestAndNeverReadsItsType()
    {
        var c = Parse("struct Dog\nfunc f(x: objref/Dog) -> bool => x is not Dog");
        AssertBound(c);
        Assert.False(c.Ownership.Analyze().IsVerified);
        var test = Test(c);
        Assert.Contains(c.Ownership.Issues, issue => issue.Source == test && issue.Failure == OwnershipFailure.Unsupported);
        Assert.DoesNotContain(c.Ownership.Issues, issue => issue.Source == test.Right);
        var body = Assert.Single(c.Ownership.Bodies, body => body.Function.Name == "f");
        Assert.Single(body.Operations, op => op.Source == test.Left && op.Kind == OwnershipOperationKind.Borrow);
        Assert.DoesNotContain(body.Operations, op => op.Source == test.Right);
        Assert.DoesNotContain(body.Operations, op => op.Source == test.Left && op.Kind == OwnershipOperationKind.Consume);
    }

    [Fact]
    public void EvaluationRetainsCallEffectsExactlyOnce()
    {
        var c = Parse("struct Dog\nfunc obtain(s: string) -> obj/Dog => obtain(s)\nfunc f(s: string) -> bool => obtain(s) is Dog");
        AssertBound(c);
        c.Ownership.Analyze();
        var test = Test(c);
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.Name == "f");
        Assert.Single(body.Operations, op => op.Source == test.Left && op.Kind == OwnershipOperationKind.Call);
        Assert.Single(body.Operations, op => op.Kind == OwnershipOperationKind.Consume);
        Assert.Empty(c.AnalyzeControlFlow(c.Binding.TypeSystem).Warnings);
    }

    [Fact]
    public void TrivialTestsRetainBothPathsAndDoNotRefineNames()
    {
        var c = Parse("struct Dog\nfunc f(x: objref/Dog) -> i32\n    if x is Dog => return 1");
        c.Bind();
        Assert.NotEmpty(c.AnalyzeControlFlow(c.Binding.TypeSystem).Issues);
        var refinement = Parse("open struct Animal\nstruct Dog: Animal\n    public func bark(self: objref/Self) -> bool => true\nfunc f(x: objref/Animal) -> bool\n    require x is Dog else => return false\n    return x.bark()");
        Assert.False(refinement.Bind().IsComplete);
        Assert.NotNull(Test(refinement).BoundRuntimeTest);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(128)]
    public void WarmBindingReusesRetainedTypesWithoutAllocation(int count)
    {
        var source = new StringBuilder("struct Dog<T>\nfunc f(x: objref/Dog<i32>)\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("    let b").Append(i).Append(" = x is not Dog<i32>\n");
        }

        var c = Parse(source.ToString());
        AssertBound(c);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        var flow = c.AnalyzeControlFlow(c.Binding.TypeSystem);
        Assert.Equal(0, AllocationMeasurement.Measure(() => flow.Reanalyze(c.Kotonoha.RootKoto)));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Bind();
            flow.Reanalyze(c.Kotonoha.RootKoto);
        }));
        AssertBound(c);
    }

    private static IsKoto Test(Compilation c)
        => Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<IsKoto>(), x => x.IsRuntimeTest);

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        return c;
    }

    private static void AssertBound(Compilation c) => Assert.True(c.Bind().IsComplete, Describe(c));

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));

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
}
