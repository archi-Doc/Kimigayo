// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8CompilationCostTest
{
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
        for (var i = 0; i < 32; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var allocated = AllocationMeasurement.Measure(() =>
        {
            if (stage == "Pipeline")
            {
                if (!c.Bind().IsComplete)
                {
                    throw new InvalidOperationException("Warm formatting Binding failed.");
                }

                c.Binding.CheckStartup(OutputKind.Application);
                if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
                {
                    throw new InvalidOperationException("Warm formatting compilation failed.");
                }

                return;
            }

            var valid = stage switch
            {
                "Binding" => c.Bind().IsComplete,
                "Ownership" => c.Ownership.Analyze().IsVerified,
                "Validation" => c.Emission.Validate(out _),
                _ => c.Emission.WriteIr(TextWriter.Null, out _),
            };
            if (!valid)
            {
                throw new InvalidOperationException("Warm formatting compilation failed.");
            }
        });
        // Eight repetitions. Binding and ownership allocate nothing; the complete
        // builtin pipeline has a 256-byte per-pass budget (measured: 216 bytes).
        Assert.InRange(allocated, 0, stage is "Binding" or "Ownership" ? 0 : 8 * 256);
    }

    [Fact]
    public void ReloadRecomputesFormattingBoundsAndSelectedCalls()
    {
        var c = MinimalEmissionTest.Analyze("let value = \"text\"\nConsole.writeLine(\"value=\\(value)\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        var replacement = MinimalEmissionTest.Analyze("let value = 42@i64\nConsole.writeLine(\"value=\\(value)\")");
        var bytes = Tinyhand.TinyhandSerializer.Serialize(replacement.Kotonoha);
        var tree = c.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.Same(c.Kotonoha, tree);
        tree!.OnDeserialized(c);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        using var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out error), error);
        Assert.Contains("alloca [26 x i8]", output.ToString());
        ScalarEmissionTest.WriteFixture("Utf8CompilationReload", output.ToString(), "value=42\n");
    }
}
