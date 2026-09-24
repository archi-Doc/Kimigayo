// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedMatchTest
{
    [Fact]
    public void SharedSubjectComposesWithParametersAndComparisonInspection()
    {
        const string Source = """
            func inspect(text: string)
                match text
                    let saved => Console.writeLine(saved)
                Console.writeLine(text)
            inspect("hello")
            let text = "hello"
            require text == (match text
                _ => "hello"
            ) else => $abort("comparison")
            """;
        NativeAllocationAudit.WriteFixture("SharedMatchParameter", Source, 0, 0, 0, "hello\nhello\n");
    }

    [Theory]
    [InlineData("Integer", "42", "40 => $abort(\"wrong\")\n    42 => Console.writeLine(\"ok\")\n    _ => $abort(\"missing\")")]
    [InlineData("Boolean", "false", "true => $abort(\"wrong\")\n    false => Console.writeLine(\"ok\")")]
    [InlineData("String", "\"hello\"", "\"other\" => $abort(\"wrong\")\n    \"hello\" => Console.writeLine(\"ok\")\n    _ => $abort(\"missing\")")]
    [InlineData("Unit", "()", "() => Console.writeLine(\"ok\")")]
    public void LiteralPatternsInspectOneReference(string name, string value, string arms)
        => NativeAllocationAudit.WriteFixture("SharedMatchLiteral" + name, "let value = " + value + "\nmatch value@ref\n    " + arms, 0, 0, 0, "ok\n");

    [Fact]
    public void BareStringSubjectAndWholeBindingRetainItsOwner()
    {
        const string Source = """
            var text = Text.toString("hello")
            match text
                "other" => $abort("wrong")
                let saved => Console.writeLine(saved)
            Console.writeLine(text)
            text = "new"
            Console.writeLine(text)
            """;
        NativeAllocationAudit.WriteFixture("SharedMatchStringBinding", Source, 1, 1, 5, "hello\nhello\nnew\n");
    }

    [Fact]
    public void WholeBindingRejectsOwnerInvalidationBeforeItsUse()
    {
        var c = MinimalEmissionTest.Analyze("var text = \"hello\"\nmatch text\n    let saved\n        text = \"new\"\n        Console.writeLine(saved)");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
