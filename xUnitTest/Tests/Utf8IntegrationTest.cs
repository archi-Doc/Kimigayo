// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class Utf8IntegrationTest
{
    [Fact]
    public void ExampleRunsUnchanged()
        => ScalarEmissionTest.EmitFixture("Utf8IntegrationExample", Read("examples/Utf8Formatting/Utf8Formatting.kimi"), "point=(3, 7)\n君: (3, 7)\nready=true\n123\n");

    [Fact]
    public void MilestoneRunsUnchanged()
        => ScalarEmissionTest.EmitFixture("Utf8IntegrationMilestone", Read("milestones/Milestone32.kimi"), "source retained\nreading destroyed\nvalue=Reading(42)\n君: value=Reading(42)\ndone=true\nMy number is 42\n");

    [Fact]
    public void OwningFailureAbortsBeforeLaterEvaluationAndConsoleOutput()
    {
        const string Source = """
            struct Value
                Self is Utf8Format
                public init() => ()
                public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
                    Console.writeLine("format once")
                    return .Err(BufferFull.init())
            func later() -> i32
                $abort("must not evaluate")
            Console.writeLine("prefix\(Value.init())\(later())")
            """;
        ScalarEmissionTest.EmitFixture("Utf8IntegrationAbort", Source, "format once\n", 1, "Hello.kimi:9:19: abort KIMI_E_FORMAT: Formatting failed\n");
    }

    [Fact]
    public void GenericFixedFormattingForwardsTypeLengthAndSourceOrigin()
    {
        const string Source = """
            func format<T, length N>(value: ref/T, bytes: uniq/[N of u8]) -> Result<Text.Utf8Slice{r}, BufferFull>
                T is Utf8Format
                origin r.source == bytes
                return Text.tryFormat(value, bytes)
            var bytes = [3 of 0@u8]
            match format(123, bytes@uniq)@move
                .Ok(let view) => Console.writeLine(view)
                .Err(_) => $abort("full")
            """;
        NativeAllocationAudit.WriteFixture("Utf8IntegrationGenericFixed", Source, 0, 0, 0, "123\n");
    }

    [Fact]
    public void ShortCircuitDoesNotSkipStaticConformanceChecks()
    {
        var c = MinimalEmissionTest.Analyze("var bytes = [0 of 0@u8]\nvar buffer = Text.fixed(bytes@uniq)\nvar writer = Text.writer(buffer@uniq)\n_ = $tryWrite(writer@uniq, \"x\\((1, 2))\")");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.UnprovenConstraint_Kd);
    }

    private static string Read(string path)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../..", path)).Replace("\r\n", "\n", StringComparison.Ordinal);
}
