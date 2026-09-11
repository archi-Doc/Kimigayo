// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class EnumBindingTest
{
    [Theory]
    [InlineData("let x = Message.Quit")]
    [InlineData("let x = Message.Write(\"text\")")]
    [InlineData("let x: Message = .Quit")]
    [InlineData("let x: Message = .Write(\"text\")")]
    [InlineData("func f() -> Message => .Write(\"text\")")]
    [InlineData("let x: Option<i32> = .Some(1)")]
    [InlineData("let x: Option<i32> = .None")]
    [InlineData("let x = Option<i32>.Some(1)")]
    [InlineData("let x = Option.Some(1)")]
    [InlineData("let x: Result<i32, string> = .Ok(1)")]
    [InlineData("let x: Result<i32, string> = .Err(\"text\")")]
    [InlineData("let x: ::Core.Option<i32> = ::Core.Option<i32>.Some(1)")]
    [InlineData("let x: Option<Option<i32>> = .Some(.Some(1))")]
    public void ConstructsQualifiedAndExpectedCases(string source)
    {
        var c = Parse("enum Message\n    Quit\n    Write(string)\n" + source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Contains(Walk(c.Kotonoha.RootKoto), x => c.Binding.TryGetEnumConstruction(x, out _));
    }

    [Theory]
    [InlineData("let x = .None")]
    [InlineData("let x: Option<i32> = .Some")]
    [InlineData("let x: Option<i32> = .None()")]
    [InlineData("let x: Option<i32> = .Some()")]
    [InlineData("let x: Option<i32> = .Some(1, 2)")]
    [InlineData("let x: Option<i32> = .Some(value: 1)")]
    [InlineData("let x: Option<i32> = .Some(\"text\")")]
    [InlineData("let x: Option<i8> = .Some(128)")]
    [InlineData("let x: Option<i32> = Option<string>.Some(\"text\")")]
    [InlineData("let x = Option.None")]
    [InlineData("let x = Option<i32>.Some")]
    [InlineData("let x = Option<i32>.None()")]
    [InlineData("let x: Option<i32> = .Missing")]
    [InlineData("let x: i32 = .Some(1)")]
    [InlineData("var v: Option<i32>\nlet x = v.None")]
    [InlineData("var v: Option<i32>\nlet x = v.Some(1)")]
    public void RejectsInvalidConstruction(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        foreach (var expression in Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>())
        {
            Assert.False(c.Binding.TryGetEnumConstruction(expression, out _));
        }
    }

    [Fact]
    public void PayloadOperationsKeepOrderAndAcquisition()
    {
        var c = Parse("enum Pair<T>\n    Pair(T, string)\nfunc f<T>(a: T, b: string) -> Pair<T> => Pair<T>.Pair(a, b)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var invocation = Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single();
        Assert.True(c.Binding.TryGetEnumConstruction(invocation, out var plan));
        Assert.Null(invocation.BoundCall);
        Assert.Equal(0, plan!.Case.Ordinal);
        Assert.Equal(AcquisitionKind.CopyOrMove, plan.Acquisitions[0]);
        Assert.Equal(AcquisitionKind.Move, plan.Acquisitions[1]);
        for (var i = 0; i < 2; i++)
        {
            Assert.Same(invocation.ArgumentNodes[i], plan.PayloadOperations[i].Source);
            Assert.Equal(i, plan.PayloadOperations[i].ParameterIndex);
        }
    }

    [Fact]
    public void CoreOptionUsesConditionalCopyAndResultUsesOrdinaryRules()
    {
        var c = Parse("var a: Option<i32>\nvar b: Option<string>\nvar r: Result<i32, i32>");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var variables = Walk(c.Kotonoha.RootKoto).OfType<FieldKoto>().ToArray();
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(variables[0].BoundType!, variables[0]));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveCopy(variables[1].BoundType!, variables[1]));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveCopy(variables[2].BoundType!, variables[2]));
    }

    [Theory]
    [InlineData("enum V<T> origin a\n    Some(ref/T from a)\n    None\nfunc f<T>(x: ref/T) -> V<T> from x => V<T>.Some(x)")]
    [InlineData("enum V<T> origin a\n    Some(ref/T from a)\n    None\nfunc f<T>(x: ref/T)\n    let v = V<T>.Some(x)")]
    [InlineData("func f<T>(x: ref/T) -> Option<ref/T from x> => .Some(x)")]
    [InlineData("func f<T>(x: uniq/T) -> Option<uniq/T from x> => .Some(x)")]
    [InlineData("enum V<T> origin a\n    Some(ref/T from a)\nfunc f()\n    var n = 1\n    let v = V<i32>.Some(n)")]
    [InlineData("enum V<T> origin a\n    Some(ref/T from a)\n    None\nfunc f<T>(x: ref/T) -> V<T> from x => .None")]
    public void PreservesPayloadOriginContracts(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Walk(c.Kotonoha.RootKoto).First(x => c.Binding.TryGetEnumConstruction(x, out _));
        Assert.True(c.Binding.TryGetEnumConstruction(use, out var plan));
        Assert.True(plan!.Type.OriginArguments.Count != 0 || plan.Type.Components[0].Origin is not null);
    }

    [Theory]
    [InlineData("enum V<T> origin a\n    Some(ref/T from a)\n    None\nlet v = V<i32>.None")]
    [InlineData("enum V<T> origin a\n    Some(ref/T from a)\nfunc f<T>(x: ref/T) -> V<T> from static => .Some(x)")]
    [InlineData("enum V\n    Some(ref/i32)")]
    [InlineData("enum E\n    A\n    A(i32)")]
    [InlineData("enum E\n    A\n    func A() => ()")]
    [InlineData("enum E\n    func A() => ()\n    A")]
    [InlineData("struct Hidden\npublic enum E\n    A(Hidden)")]
    [InlineData("enum E\n    A\nenum E\n    B")]
    [InlineData("enum E<T>\n    A(T)\nenum E<T>\n    B")]
    [InlineData("enum E<T>\n    T is Copy\n    A(T)\nlet x = E.A(\"text\")")]
    [InlineData("enum E<T>\n    A(T)\n    public func f() => ()\nE.f()")]
    public void RejectsInvalidDeclarationsAndOriginContracts(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("enum E<T>\n    Both(T, T)\nvar b: i8\nlet x = E.Both(1, b)")]
    [InlineData("enum E<T>\n    Both(T, T)\nvar b: i8\nlet x = E.Both(b, 1)")]
    [InlineData("enum E<T>\n    T is Copy\n    A(T)\nlet x = E.A(1)")]
    [InlineData("enum E\n    A\n    public func f() -> i32 => 1\nlet x = E.f()")]
    [InlineData("enum Option<T>\n    Mine(T)\nlet x = Option<i32>.Mine(1)\nlet y: ::Core.Option<i32> = .Some(1)")]
    public void SharesInferenceConstraintsAndMemberLookup(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void RebindInvalidatesConstructionAfterCaseCollision()
    {
        var c = Parse("enum E\n    A(i32)\nlet x = E.A(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single();
        Assert.True(c.Binding.TryGetEnumConstruction(call, out var plan));
        var declaration = (EnumKoto)plan!.Case.Owner.Declaration;
        c.Kotonoha.CreateCodeContext().Parse(declaration, "func A() => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.TryGetEnumConstruction(call, out _));
    }

    [Theory]
    [InlineData(CoreDeclarationId.Option)]
    [InlineData(CoreDeclarationId.Result)]
    public void CoreShapeMutationIsRejected(CoreDeclarationId id)
    {
        var c = Parse("let x: Option<i32> = .Some(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var symbol = c.Core.GetSymbol(id)!;
        c.Core.Kotonoha.CreateCodeContext().Parse((EnumKoto)symbol.Declaration, "Extra");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(CoreDeclarationState.Invalid, c.Core.Declarations[(int)id].State);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidCoreIntrinsics_Kd);
    }

    [Fact]
    public void RepeatedCoreEnumHeaderInvalidatesCatalogEntry()
    {
        var c = Parse("let x: Option<i32> = .Some(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        c.Core.Kotonoha.CreateCodeContext().Parse(c.Core.Kotonoha.RootKoto, "public enum Option<T>");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(CoreDeclarationState.Invalid, c.Core.Declarations[(int)CoreDeclarationId.Option].State);
    }

    [Fact]
    public void WarmEnumBindingReusesIdentitiesAndPlanStorage()
    {
        var c = Parse("enum E<T>\n    Both(T, string)\n" + string.Join('\n', Enumerable.Range(0, 128).Select(i => $"let v{i} = E<i32>.Both({i}, \"x\")")));
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
        }

        var call = Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().First();
        Assert.True(c.Binding.TryGetEnumConstruction(call, out var plan));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
        Assert.True(c.Binding.TryGetEnumConstruction(call, out var rebound));
        Assert.Same(plan, rebound);
    }

    [Theory]
    [InlineData("func f(x: Option<i32>) => ()\nf(.Some(1))", true)]
    [InlineData("func f(x: Option<i32>) => ()\nf(.None)", true)]
    [InlineData("func f(x: Option<Option<i8>>) => ()\nf(.Some(.Some(127)))", true)]
    [InlineData("func f(x: Option<i8>) => ()\nf(.Some(128))", false)]
    [InlineData("func f(x: Option<i8>) => ()\nfunc f(x: Option<i32>) => ()\nf(.Some(128))", true)]
    [InlineData("func f(x: Option<i32>) => ()\nfunc f(x: Option<i8>) => ()\nf(.Some(128))", true)]
    [InlineData("func f(x: Option<i32>) => ()\nfunc f(x: Option<i8>) => ()\nf(.None)", false)]
    [InlineData("func f<T>(x: Option<T>, y: T) => ()\nf(.Some(1), 2)", true)]
    [InlineData("func f<T>(x: T) => ()\nfunc f(x: Option<i32>) => ()\nf(.None)", false)]
    public void CallCandidatesProbeContextualCasesWithoutCommittingLosers(string source, bool valid)
    {
        var c = Parse(source);
        Assert.Equal(valid, c.Bind().IsComplete);
        var call = Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().First();
        Assert.Equal(valid, call.BoundCall is not null);
        if (!valid)
        {
            Assert.DoesNotContain(Walk(c.Kotonoha.RootKoto), x => c.Binding.TryGetEnumConstruction(x, out _));
        }
    }

    [Fact]
    public void WarmContextualConstructionPreservesBorrowOperations()
    {
        var c = Parse("func take(x: Option<ref/i32 from static>) => ()\nfunc f(x: ref/i32 from static)\n    take(.Some(x))");
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Bind().IsComplete, Describe(c));
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind()));
    }

    [Fact]
    public void BindingConstructionDoesNotCertifyOwnership()
    {
        var c = Parse("let x: Option<i32> = .Some(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.False(c.Ownership.Analyze().IsVerified);
    }

    [Theory]
    [InlineData("    let value: i32")]
    [InlineData("    property value: i32 { get }")]
    [InlineData("    init() => ()")]
    [InlineData("    deinit() => ()")]
    [InlineData("    struct Nested")]
    public void ForbiddenEnumMembersCannotPassParsingAndBinding(string member)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "enum E\n    A\n" + member);
        Assert.True(c.Kotonoha.DiagnosticCollection.GetArray().Length != 0 || !c.Bind().IsComplete);
    }

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

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node} ({x.Node.GetType().Name}, parent {x.Node.Parent?.GetType().Name})"));
}
