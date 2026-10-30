// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8CompilationCostTest
{
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Binding")]
    [InlineData("Ownership")]
    [InlineData("Emission")]
    [InlineData("Validation")]
    [InlineData("Pipeline")]
    public void WarmFormattingCompilationHasBoundedAllocation(string stage)
    {
        const string Source = """
            let owned = "number=\(42)"
            Console.writeLine(owned)
            Console.writeLine("My number is \(42@i64)")
            var bytes = [64 of 0@u8]
            var buffer = Text.fixed(bytes@uniq)
            var writer = Text.writer(buffer@uniq)
            _ = $tryWrite(writer@uniq, "number=\(42)")
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var allocated = AllocationMeasurement.Measure(() =>
        {
            if (stage == "Pipeline")
            {
                if (!c.Bind().IsComplete)
                {
                    throw new InvalidOperationException("Warm formatting Binding failed.");
                }

                c.Binding.CheckStartup(OutputKind.Application);
                if (!c.Analyze().IsVerified || !c.Generate(TextWriter.Null))
                {
                    throw new InvalidOperationException("Warm formatting compilation failed.");
                }

                return;
            }

            var valid = stage switch
            {
                "Binding" => c.Bind().IsComplete,
                "Ownership" => c.Analyze().IsVerified,
                "Validation" => c.Generate(TextWriter.Null),
                _ => c.Generate(TextWriter.Null),
            };
            if (!valid)
            {
                throw new InvalidOperationException("Warm formatting compilation failed.");
            }
        });
        // Binding and ownership allocate nothing; the complete builtin pipeline has a
        // 256-byte per-pass budget (measured: 216 bytes).
        Assert.InRange(allocated, 0, stage is "Binding" or "Ownership" ? 0 : AllocationMeasurement.MeasuredOperations * 256);
    }

    [Fact]
    public void ReloadRecomputesFormattingBoundsAndSelectedCalls()
    {
        var c = MinimalEmissionTest.Analyze("let value = \"text\"\nConsole.writeLine(\"value=\\(value)\")");
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var replacement = MinimalEmissionTest.Analyze("let value = 42@i64\nConsole.writeLine(\"value=\\(value)\")");
        var bytes = Tinyhand.TinyhandSerializer.Serialize(replacement.Kotonoha);
        var tree = c.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.Same(c.Kotonoha, tree);
        tree!.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Analyze().IsVerified);
        using var output = new StringWriter();
        Assert.True(c.Generate(output), MinimalEmissionTest.Describe(c));
        Assert.Contains("alloca [26 x i8]", output.ToString());
        ScalarEmissionTest.WriteFixture("Utf8CompilationReload", output.ToString(), "value=42\n");
    }
}
