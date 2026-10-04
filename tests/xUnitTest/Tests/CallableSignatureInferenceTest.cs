// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class CallableSignatureInferenceTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Item", "func answer() -> i32 => 42\nlet value = consume(answer)")]
    [InlineData("Closure", "let answer = func [] () -> i32 => 42\nlet value = consume(answer)")]
    [InlineData("Common", "let answer: () -> i32 = func [] () -> i32 => 42\nlet value = consume(answer@move)")]
    public void IndependentlyTypedCallableResultsInferOuterSlots(string name, string expression)
    {
        var source = "func consume<T, F>(action: F) -> T\n    F is Callable<owner, () -> T>\n    return action@move()\n" + expression + "\nrequire value == 42 else => $abort(\"inference\")";
        ScalarEmissionTest.EmitFixture("CallableSignature" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("Shared", "func invoke<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\nrequire invoke(answer) == 42 else => $abort(\"shared\")")]
    [InlineData("Exclusive", "func invoke<T, F>(f: uniq/F) -> T\n    F is Callable<uniq, () -> T>\n    return f()\nvar f = answer\nrequire invoke(f@uniq) == 42 else => $abort(\"exclusive\")")]
    [InlineData("Input", "func input<T, F>(f: ref/F) -> i32\n    F is Callable<(T) -> i32>\n    return 42\nfunc identity(value: i64) -> i32 => 42\nrequire input(identity) == 42 else => $abort(\"input\")")]
    [InlineData("Forward", "func invoke<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\nfunc forward<G>(f: ref/G) -> i32\n    G is Callable<() -> i32>\n    return invoke(f)\nrequire forward(answer) == 42 else => $abort(\"forward\")")]
    [InlineData("Tuple", "func invoke<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\nfunc pair() -> (i32, bool) => (42, true)\nlet result = invoke(pair)\nrequire result.0 == 42 and result.1 else => $abort(\"tuple\")")]
    public void SignaturesInferThroughEachReceiverAndGenericForwarding(string name, string body)
        => ScalarEmissionTest.EmitFixture("CallableSignature" + name, "func answer() -> i32 => 42\n" + body, string.Empty);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignatureEvidencePrecedesLiteralDefaultsInEitherOrder(bool reversed)
    {
        var parameters = reversed ? "value: T, f: ref/F" : "f: ref/F, value: T";
        var invocation = reversed ? "accept(0, answer)" : "accept(answer, 0)";
        var source = "func answer() -> i64 => 42\nfunc need(value: i64) => require value == 42 else => $abort(\"type\")\nfunc accept<T, F>(" + parameters + ") -> T\n    F is Callable<() -> T>\n    return f()\nlet result = " + invocation + "\nneed(result)";
        ScalarEmissionTest.EmitFixture("CallableSignatureLiteral" + reversed, source, string.Empty);
    }

    [Fact]
    public void AnInferredNonCopyResultKeepsItsDestructionResponsibility()
    {
        const string Source = """
            struct Item
                drop => Console.writeLine("drop")
            func make() -> Item => Item.init()
            func consume<T, F>(action: F) -> T
                F is Callable<owner, () -> T>
                return action@move()
            let result = consume(make)
            Console.writeLine("alive")
            """;
        ScalarEmissionTest.EmitFixture("CallableSignatureNonCopy", Source, "alive\ndrop\n");
    }

    [Fact]
    public void ContainerArgumentsAreSubstitutedBeforeSignatureMatching()
    {
        const string Source = """
            struct Box<T>
                public func apply<R, F>(self: ref/Self, f: ref/F, value: T) -> R
                    F is Callable<(T) -> R>
                    return f(value@move)
            func widen(value: i32) -> i64 => value@i64
            func need(value: i64) => require value == 42 else => $abort("container")
            let box = Box<i32>.init()
            let result = box.apply(widen, 42)
            need(result)
            func flag(value: bool) -> i32 => if value => 7 else => 0
            let other = Box<bool>.init()
            let small = other.apply(flag, true)
            require small == 7 else => $abort("other instance")
            """;
        ScalarEmissionTest.EmitFixture("CallableSignatureContainer", Source, string.Empty);
    }

    [Fact]
    public void AFixedContainerInputCannotBeInferredFromTheCallable()
    {
        const string Source = """
            struct Box<T>
                public func accept<R, F>(self: ref/Self, f: ref/F)
                    F is Callable<(T) -> R>
                    return
            func flag(value: bool) -> i64 => 42
            let box = Box<i32>.init()
            box.accept(flag)
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("NoApplicableOverload_Kd", error.Code);
        Assert.Contains("(bool) -> i64", error.Note, StringComparison.Ordinal);
        Assert.Contains("(i32) -> R", error.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingOrdinaryEvidenceRejectsInEitherArgumentOrder(bool reversed)
    {
        var parameters = reversed ? "value: T, f: ref/F" : "f: ref/F, value: T";
        var invocation = reversed ? "accept(true, answer)" : "accept(answer, true)";
        var source = "func answer() -> i32 => 42\nfunc accept<T, F>(" + parameters + ")\n    F is Callable<() -> T>\n    return\n" + invocation;
        var path = Path.GetFullPath("callable-signature.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.NoApplicableOverload_Kd), error.Code);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(invocation, source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal("none of 1 candidates applies", error.Label);
        var fact = Assert.Single(error.Reason!);
        Assert.Equal("candidates", fact.Name);
        Assert.Equal("1", fact.Value);
        Assert.Equal("candidate", Assert.Single(error.Related!).Role);
        Assert.Contains("known call signature is () -> i32", error.Note, StringComparison.Ordinal);
        Assert.Contains("requires () -> bool", error.Note, StringComparison.Ordinal);
        Assert.Null(error.Advice);
        Assert.Null(error.Repairs);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains(invocation, console.Text, StringComparison.Ordinal);
        Assert.Contains(error.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains(error.Message, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void AnAnonymousWaitingBodyDoesNotInferItsOuterResult()
    {
        const string Source = "func consume<T, F>(action: F) -> T\n    F is Callable<owner, () -> T>\n    return action@move()\nlet value = consume(func [] () => 42)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("accept(missing, true)", "UnresolvedBinding_Kd")]
    [InlineData("accept(answer, true)\nlet independent: i32 = true", "NoApplicableOverload_Kd,TypeMismatch_Kd")]
    public void MissingPrerequisitesAndIndependentErrorsKeepTheirCauses(string body, string codes)
    {
        var source = "func answer() -> i32 => 42\nfunc accept<T, F>(f: ref/F, value: T)\n    F is Callable<() -> T>\n    return\n" + body;
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        Assert.Equal(codes.Split(','), TestDiagnostics.Of(c).Select(x => x.Code));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSignatureInferenceAndEmissionAllocateNothing()
    {
        const string Source = "func answer() -> i32 => 42\nfunc invoke<T, F>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\nrequire invoke(answer) == 42 else => $abort(\"warm\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
