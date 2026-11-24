// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericCallFormationBindingTest
{
    // PLAN G464, E3e ruling 13 (SPEC 23.1.2, 15.3.4, 8.3): a use whose own syntax holds a written Type with a failed occurrence inside a
    // Function Type rests on it, as an explicit argument's call does (below): a reference's explicit Type argument (t5), a constructor's
    // (t6), a Case's (t6e) and a payload-less Case's (t10) Type-application qualifier, a member call's explicit Type argument (t7). Each
    // records only the written `Box<string>`'s UnsatisfiedConstraint_Kd (DECISIONS Q13, rule W: dev's restating UnsatisfiedConstraint_Kd
    // or NoApplicableOverload_Kd at the use goes) and publishes no decision of its own.
    [Theory]
    [InlineData("func ku<U>(f: U) -> i32 => 0\nfunc host() -> i32\n    let g = ku<() -> Box<string>>\n    return 0\n", "ku<() -> Box<string>>")] // t5
    [InlineData("struct Holder<U>\n    public let u: i32\n    public init(u: i32) => self.u = u\nfunc host() -> i32\n    let h = Holder<() -> Box<string>>.init(u: 1)\n    return 0\n", "Holder<() -> Box<string>>.init(u: 1)")] // t6
    [InlineData("enum E<U>\n    C(i32)\n    N\nfunc host() -> i32\n    let e = E<() -> Box<string>>.C(1)\n    return 0\n", "E<() -> Box<string>>.C(1)")] // t6e
    [InlineData("enum E<U>\n    C(i32)\n    N\nfunc host() -> i32\n    let e = E<() -> Box<string>>.N\n    return 0\n", "E<() -> Box<string>>.N")] // t10
    [InlineData("struct S\n    public init() => ()\n    public func take<V>(self: ref/Self) => ()\nfunc host(s: ref/S) -> i32\n    s.take<() -> Box<string>>()\n    return 0\n", "s.take<() -> Box<string>>()")] // t7
    public void AFailedOccurrenceInAUsesWrittenTypeRestsTheUse(string program, string use)
    {
        var source = "struct Box<T>\n    T is i32\n" + program;
        Assert.Equal("1*UnsatisfiedConstraint_Kd", DiagnosticCorpus.RecordProfile(source));
        var c = MinimalEmissionTest.Analyze(source);
        var node = KotoTree.Walk(c.Kotonoha.RootKoto).First(x => x is InvocationKoto or GenericsKoto or MemberAccessKoto && x.Span.Length == use.Length && source.Substring(x.Span.Start, x.Span.Length) == use);
        Assert.NotEqual(BindingState.Resolved, node.StateOf());
    }

    // PLAN G464, E3e ruling 15 (a) (SPEC 23.1.2, 15.2.3; R7-C2e2 e2-5a): a use rests only on the written Type syntax under its own
    // syntax; a declaration's written Type keeps the states its occurrences' judgments give it. `needOwned(r)` proves R<i32> Owned by
    // reading R's stored Type, whose `S<R<Wrap<T>>>` is unproven under an Array layer, so the use keeps its own UnprovenConstraint_Kd
    // beside the Field's and the written occurrence's (rf3; ComponentSummaryTest counts the family's records).
    [Fact]
    public void AUseReadingADeclarationsWrittenTypeKeepsItsRecord()
    {
        var source = "struct S<U>\n    U is Owned\n    public var u: U\nstruct Wrap<A>\n    public var a: A\nstruct R<T>\n    let next: Array<S<R<Wrap<T>>>>\n" +
            "func needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(r: ref/R<i32>) -> ()\n    needOwned(r)\npublic func main() => ()\n";
        Assert.Equal(
            ["UnprovenConstraint_Kd let next: Array<S<R<Wrap<T>>>>", "UnprovenConstraint_Kd S<R<Wrap<T>>>", "UnprovenConstraint_Kd needOwned(r)"],
            DiagnosticCorpus.Check(source).Diagnostics.Select(x => x.Span is { } span ? $"{x.Code} {source.Substring(span.Start, span.Length)}" : x.Code));
    }

    // PLAN G464, E3e ruling 15 (b) (SPEC 23.1.2): a conversion's target is written Type syntax under its own syntax, so a conversion
    // whose target failed, for any cause, rests on it: here each `objref/Base` is MissingOriginBinding_Kd (a moved value supplies no
    // Origin), the conversion writes nothing and the Loans over it record nothing (DECISIONS Q13: dev's two ComparisonLoanConflict_Kd,
    // dependent records, go; MirValidatorTest's R5-M72 row borrows instead).
    [Fact]
    public void AFailedConversionTargetRestsTheConversion()
    {
        var source = "open struct Base\n    public let value: i32 = 9\nstruct Derived: Base\n    public init() => ()\nfunc own(x: objref/Base) -> () => ()\n" +
            "func probe<s/T>(flag: bool, a: s/Derived, b: s/Derived) -> ()\n    s is obj\n    own(if flag => a@move@objref/Base else => b@move@objref/Base)\n";
        Assert.Equal("2*MissingOriginBinding_Kd", DiagnosticCorpus.RecordProfile(source));
    }

    // PLAN G464, O-8 (a) (SPEC 23.1.2, 15.3.4, 8.3): an explicit Type argument whose written occurrence inside a Function Type failed
    // rests the call (no plan, InvalidExplicitArgumentCannotPublishCall) and the call records nothing of its own (DECISIONS Q13, rule W:
    // dev's NoApplicableOverload_Kd at the call, from the call's own formation of the nested result, goes), inside an Option too (o8c2).
    [Theory]
    [InlineData("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<() -> Box<string>>()\n", "1*UnsatisfiedConstraint_Kd")] // o8c
    [InlineData("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<Option<() -> Box<string>>>()\n", "1*UnsatisfiedConstraint_Kd")] // o8c2
    [InlineData("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<() -> Box<i32>>()\n", "clean")] // o8v
    public void AFailedOccurrenceInAnExplicitArgumentRestsTheCall(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    [Theory]
    [InlineData("Box<string>")]
    [InlineData("(i32, Box<string>)")]
    [InlineData("[2 of Box<string>]")]
    [InlineData("raw/Box<string>")]
    [InlineData("() -> Box<string>")]
    public void InvalidExplicitArgumentCannotPublishCall(string argument)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<" + argument + ">()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd);
        Assert.Null(Call(c).CallOf());
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Call(c).CallOf());
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Null(Call(restored).CallOf());
    }

    [Fact]
    public void InvalidInferredArgumentCannotPublishCall()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>(value: T) => ()\n    func call(value: Box<string>) => take(value)");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Call(c).CallOf());
    }

    [Theory]
    [InlineData("Box<i32>")]
    [InlineData("(i32, Box<i32>)")]
    [InlineData("[2 of Box<i32>]")]
    [InlineData("raw/Box<i32>")]
    [InlineData("() -> Box<i32>")]
    public void ValidExplicitArgumentsRemainApplicable(string argument)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<" + argument + ">()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.NotNull(Call(c).CallOf());
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.NotNull(Call(restored).CallOf());
    }

    [Theory]
    [InlineData("take<Box<U>>(value@move)")]
    [InlineData("take(value@move)")]
    public void DependentArgumentsUseCallerEvidence(string expression)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is Copy\ngroup Consumer\n    func take<T>(value: T) => ()\n    func call<U>(value: Box<U>)\n        U is Copy\n        " + expression);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void InvalidDeclarationContextCannotSupplyArgument()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\n    public struct S\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<Invalid.S>()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Call(c).CallOf());
    }

    [Fact]
    public void ChangingArgumentRevokesAndRestoresSameCallPlan()
    {
        const string source = "struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<Box<i32>>()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var call = Call(c);
        var plan = call.CallOf();
        Assert.NotNull(plan);
        var generic = Assert.IsType<GenericsKoto>(call.Method);
        var original = Assert.Single(generic.TypeArguments);
        var donor = MinimalEmissionTest.Analyze(source.Replace("Box<i32>", "Box<string>", StringComparison.Ordinal));
        var replacement = Assert.Single(Assert.IsType<GenericsKoto>(Call(donor).Method).TypeArguments);
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(generic, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.Null(call.CallOf());
        Assert.True(KotoHelper.Replace(generic, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(plan, call.CallOf());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmGenericCallChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ngroup Consumer\n    func take<T>() => ()\n    func call() => take<Box<i32>>()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Generic call formation failed.");
            }
        }));
    }

    private static InvocationKoto Call(Compilation c)
        => Assert.IsType<InvocationKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single(x => x.Name == "call").ExpressionBody);
}
