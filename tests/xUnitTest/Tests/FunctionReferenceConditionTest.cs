// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class FunctionReferenceConditionTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExpectedFunctionTypesKeepTheReferencedConditions(bool block, bool virtualSlot)
    {
        foreach (var type in new[] { "string", "i32" })
        {
            var path = Path.GetFullPath("reference-condition.kimi");
            var source = Program(block, virtualSlot, type);
            var c = MinimalEmissionTest.Analyze(source, path);
            Assert.Equal(type == "i32", c.Binding.Result.IsComplete);
            if (type == "i32")
            {
                ScalarEmissionTest.EmitFixture($"FunctionReferenceCondition{block}{virtualSlot}", source, string.Empty);
                continue;
            }

            c.Binding.ReportDiagnostics();
            c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
            var result = c.Diagnostics.Finalize();
            var error = Assert.Single(result.Diagnostics);
            Assert.Equal("UnsatisfiedConstraint_Kd", error.Code);
            Assert.Contains(error.Reason!, x => x.Name == "subject" && x.Value == "string");
            Assert.Contains(error.Reason!, x => x.Name == "constraint" && x.Value == "T is Copy");
            Assert.Single(error.Related!, x => x.Role == "constraint");
            var console = new DiagnosticContractTest.DiagnosticConsole();
            new Kimigayo(console).Render(result, string.Empty);
            Assert.Contains("Copy", console.Text, StringComparison.Ordinal);
            output.WriteLine(console.Text);
            var identity = SourceIdentity.FromPath(path);
            foreach (var related in new[] { false, true })
            {
                var shown = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
                Assert.Contains("Copy", shown.Message, StringComparison.Ordinal);
                Assert.Contains("string", shown.Message, StringComparison.Ordinal);
                output.WriteLine(shown.Message);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownConditionsNeverSelectAnotherReference(bool overload)
    {
        var source = "struct Box<T>\n    public func choose(value: (i32) -> i32) -> i32\n        T is Copy\n        return 1\n" +
            (overload ? "    public func choose(value: (i32) -> Never) -> i32 => 2\n" : string.Empty) +
            "func use<T>()\n    T is Owned\n    let f: ((i32) -> Never) -> i32 = Box<T>.choose\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnprovenConstraint_Kd", error.Code);
        Assert.Contains("Copy", error.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("func keep<T>(value: T) -> T\n    T is Owned\n    return value@move\nlet h: (ref/i32) -> ref/i32 = keep", "T is Owned", "UnprovenConstraint_Kd")]
    [InlineData("func pair<T>(value: T) -> (T, T)\n    T is Copy\n    return (value, value)\nlet f: (string) -> (string, string) = pair", "T is Copy", "UnsatisfiedConstraint_Kd")]
    // Owned needs a static Origin proof; an unproven condition also precedes the met binding's Origin conflict.
    [InlineData("func firstOf<T>(a: T, b: ref/i32) -> T\n    T is Owned\n    return a@move\nlet f: (ref/i32, ref/i32) -> ref/i32 = firstOf", "T is Owned", "UnprovenConstraint_Kd")]
    public void FailedGenericConditionsKeepTheirConcreteObligation(string source, string clause, string code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(code, error.Code);
        Assert.Contains(clause, error.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("per-call", error.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownOwnSlotConditionsBlockReferenceSelection()
    {
        var c = MinimalEmissionTest.Analyze("func pick<T>(value: T) -> i32\n    T is Copy\n    return 1\nfunc use<U>(value: U)\n    U is Owned\n    let f: (U) -> i32 = pick\n()");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnprovenConstraint_Kd", error.Code);
        Assert.Contains("T is Copy", error.Note, StringComparison.Ordinal);
        Assert.Contains("U", error.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("i32")]
    public void RefutedCandidatesDoNotPreventAnApplicableReference(string type)
    {
        var source = "func fail(value: i32) -> Never => $abort(\"not called\")\nstruct Box<T>\n    public func choose(value: (i32) -> i32) -> i32\n        T is Copy\n        return 1\n    public func choose(value: (i32) -> Never) -> i32 => 2\n" +
            $"let f: ((i32) -> Never) -> i32 = Box<{type}>.choose\nlet value: (i32) -> Never = fail\nrequire f(value@move) == 2 else => $abort(\"choice\")";
        ScalarEmissionTest.EmitFixture("FunctionReferenceConditionChoice" + type, source, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ConditionChecksReuseBindingStorage()
    {
        var c = MinimalEmissionTest.Analyze(Program(true, true, "i32"));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }

    private static string Program(bool block, bool virtualSlot, string type)
    {
        var receiver = virtualSlot ? "self: objref/Self" : string.Empty;
        var function = $"public {(virtualSlot ? "virtual " : string.Empty)}func read({receiver}) -> i32";
        var member = block ? $"    Self is Marker when T is Copy\n        {function} => 1\n"
            : $"    {function}\n        T is Copy\n        return 1\n";
        var inputs = virtualSlot ? $"objref/Box<{type}>" : string.Empty;
        var call = virtualSlot ? $"let owner = Box<{type}>.init()@obj\nrequire action(owner@objref) == 1 else => $abort(\"reference\")" : "require action() == 1 else => $abort(\"reference\")";
        return $"contract Marker\n{(virtualSlot ? "open " : string.Empty)}struct Box<T>\n    public init() => ()\n" + member + $"let action: ({inputs}) -> i32 = Box<{type}>.read\n" + call;
    }
}
