// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class SharedMatchTest
{
    [Fact]
    public void CopyPayloadSnapshotAndDestructibleBorrowKeepSeparateResponsibilities()
    {
        const string Source = """
            struct Pair
                Self is Copy
                public var number: i32
                public init(number: i32) => self.number = number
            struct Item
                public let number: i32
                public init(number: i32) => self.number = number
                deinit => Console.writeLine("drop")
            let value = (Pair.init(42), Item.init(7))
            match value
                (var pair, let item)
                    pair.number = 0
                    require pair.number == 0 and item.number == 7 else => $abort("bindings")
            require value.0.number == 42 else => $abort("snapshot")
            Console.writeLine("done")
            """;
        NativeAllocationAudit.WriteFixture("SharedMatchCopyAndBorrow", Source, 0, 0, 0, "done\ndrop\n");
    }

    [Fact]
    public void ASelectedArmCanReplaceTheOwnerAfterItsLastBorrowedUse()
    {
        const string Source = """
            enum E
                Some(string)
                None
            var value: E = .Some(Text.toString("hello"))
            match value
                .Some(let text)
                    Console.writeLine(text)
                    value = .None
                .None => $abort("missing")
            match value
                .Some(_) => $abort("old")
                .None => Console.writeLine("done")
            """;
        NativeAllocationAudit.WriteFixture("SharedMatchLastUse", Source, 1, 1, 5, "hello\ndone\n");
    }

    [Fact]
    public void SharedTupleBindingsCopyAndBorrowWithoutDecomposingTheOwner()
    {
        const string Source = """
            let value = (Text.toString("hello"), 42)
            match value
                ("wrong", _) => $abort("wrong")
                (let text, var number)
                    number += 1
                    require number == 43 else => $abort("copy")
                    Console.writeLine(text)
            require value.1 == 42 else => $abort("owner")
            Console.writeLine(value.0)
            """;
        NativeAllocationAudit.WriteFixture("SharedMatchTuple", Source, 1, 1, 5, "hello\nhello\n");
    }

    [Fact]
    public void NestedReferencePatternsFollowTheirOwnAddresses()
    {
        const string Source = """
            enum E<T>
                Some(T)
                None
            let text = "hello"
            let savedText = text@ref
            let inner = (savedText, 42)
            let input = inner@ref
            let value: E<ref/(ref/string during savedText, i32) during (input and savedText)> = .Some(input)
            match value@ref
                .Some(("wrong", _)) => $abort("wrong")
                .Some((let saved, let number))
                    require number == 42 else => $abort("number")
                    Console.writeLine(saved)
                .None => $abort("none")
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.True(c.Ownership.Result.IsVerified, string.Join("\n", c.Binding.Obligations));
        NativeAllocationAudit.WriteFixture("SharedMatchNestedReferences", Source, 0, 0, 0, "hello\n");
    }

    [Fact]
    public void InactiveEnumPayloadDoesNotDereferenceItsStorage()
    {
        const string Source = """
            enum E
                Some(string)
                None
            let value: E = .None
            match value
                .Some("wrong") => $abort("payload")
                .Some(_) => $abort("case")
                .None => Console.writeLine("ok")
            """;
        NativeAllocationAudit.WriteFixture("SharedMatchInactivePayload", Source, 0, 0, 0, "ok\n");
    }

    [Fact]
    public void SharedComponentKeepsTheWholeOwnerProtected()
    {
        var c = MinimalEmissionTest.Analyze("var value = (\"hello\", 1)\nmatch value\n    (let saved, _)\n        value = (\"new\", 2)\n        Console.writeLine(saved)");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

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
