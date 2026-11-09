// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

public class CallConstraintFailureTest
{
    private const string FailedAtCallAndReference = """
        contract Z
            func z(self: ref/Self) -> i32
        struct S
            i32 is Z
            public init() => ()
        func f(s: ref/S) -> i32 => 1
        func use(s: ref/S) -> i32
            let g: (ref/S) -> i32 = f
            return f(s) + g(s)
        """;

    private const string FailedAtMatching = """
        contract C
            func m(self: ref/Self) -> i32
        struct S
            public init() => ()
        struct Box<T>
            T is C
            public init() => ()
        contract D
            func n(self: ref/Self, b: ref/Box<S>) -> i32
        struct U
            Self is D
            public init() => ()
            public func n(self: ref/Self, b: ref/Box<S>) -> i32 => 1
        func use<T>(t: ref/T, b: ref/Box<S>) -> i32
            T is D
            return t.n(b)
        """;

    private const string FailedWithACycle = """
        contract Z
            func z(self: ref/Self) -> i32
        contract C
            func m(self: ref/Self, other: ref/Box<Self>, w: ref/Zed<S>) -> i32
        struct Box<T>
            T is C
            public init() => ()
        struct Zed<T>
            T is Z
            public init() => ()
        struct S
            Self is C
            public init() => ()
            public func m(self: ref/Self, other: ref/Box<S>, w: ref/Zed<S>) -> i32 => 1
        func use(s: ref/S, b: ref/Box<S>, w: ref/Zed<S>) -> i32 => s.m(b, w)
        """;

    // PLAN G375: an answer other than Proven is computed at each read, in the reader's own scope, at a call and a reference.
    [Theory]
    [InlineData(FailedAtCallAndReference, "1*InvalidConstraint_Kd 2*UnsatisfiedConstraint_Kd")]
    [InlineData(FailedAtMatching, "1*NoApplicableOverload_Kd 3*UnsatisfiedConstraint_Kd")]
    [InlineData(FailedWithACycle, "1*NoApplicableOverload_Kd 3*UnsatisfiedConstraint_Kd")]
    public void AFailedSignatureIsComputedAtEachRead(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    [Theory]
    [InlineData("string", false, false)]
    [InlineData("string", true, false)]
    [InlineData("i32", false, true)]
    [InlineData("i32", true, true)]
    public void CallsKeepTheSubstitutedFailedCondition(string type, bool block, bool valid)
    {
        var member = block ? "    Self is Marker when T is Copy\n        public func read() -> i32 => 1\n"
            : "    public func read() -> i32\n        T is Copy\n        return 1\n";
        var source = "contract Marker\nstruct S<T>\n" + member + $"let result = S<{type}>.read()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        if (!valid)
        {
            var record = Assert.Single(TestDiagnostics.Of(c));
            Assert.Equal("NoApplicableOverload_Kd", record.Code);
            Assert.Contains("Copy", record.Note, StringComparison.Ordinal);
            Assert.Contains("string", record.Note, StringComparison.Ordinal);
            var published = Assert.Single(c.Diagnostics.Finalize().Diagnostics);
            var clause = Assert.Single(published.Related!, x => x.Role == "constraint");
            Assert.Equal("T is Copy", source.Substring(clause.Span!.Value.Start, clause.Span.Value.Length));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnreachedConditionDoesNotExplainAnInputMismatch(bool overload)
    {
        var source = "func read<T>(value: bool) -> i32\n    T is Copy\n    return 1\n" +
            (overload ? "func read<T>(value: i32) -> i32\n    T is Copy\n    return 2\n" : string.Empty) +
            "let result = read<string>(\"wrong\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("NoApplicableOverload_Kd", record.Code);
        Assert.DoesNotContain("refuted", record.Note ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedConditionsDoNotDisplaceAnApplicableOverload()
    {
        var c = MinimalEmissionTest.Analyze("func read<T>(value: ref/T) -> i32\n    T is Copy\n    return 1\nfunc read(value: ref/string) -> i32 => 2\nlet text = \"text\"\nlet result = read(text@ref)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Empty(c.Binding.Issues);
    }
}
