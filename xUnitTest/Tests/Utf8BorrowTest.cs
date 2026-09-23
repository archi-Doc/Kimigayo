// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class Utf8BorrowTest
{
    private const string Fixed = "var storage = [4 of 0@u8]\nvar buffer = Text.fixed(storage@uniq)\n";

    [Theory]
    [InlineData("buffer.text()")]
    [InlineData("(buffer@move).intoText()")]
    public void SharedViewsRetainExclusiveSourceAuthority(string view)
    {
        var c = MinimalEmissionTest.Analyze(Fixed + "let result = " + view + "\n_ = storage[0]\n_ = result@move");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Theory]
    [InlineData("buffer.text()")]
    [InlineData("(buffer@move).intoText()")]
    public void ViewLastUseReleasesSource(string view)
    {
        var c = MinimalEmissionTest.Analyze(Fixed + "let result = " + view + "\n_ = result@move\n_ = storage[0]");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("Local", "let text = \"ab\"\nlet view = Text.utf8(text)\nConsole.writeLine(view)\nConsole.writeLine(text)", "ab\nab\n")]
    [InlineData("NestedTemporary", "Console.writeLine(Text.utf8(\"ab\"))", "ab\n")]
    [InlineData("Forward", "func view(value: ref/string) -> Text.Utf8Slice => Text.utf8(value)\nlet text = \"ab\"\nConsole.writeLine(view(text))", "ab\n")]
    public void StringViews(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("Utf8Borrow" + name, source, stdout);

    [Theory]
    [InlineData("let text = \"ab\"\nlet view = Text.utf8(text)\n_ = text@move\nConsole.writeLine(view)")]
    [InlineData("let view = Text.utf8(\"ab\")\nConsole.writeLine(view)")]
    public void StringViewCannotOutliveItsOwner(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Fact]
    public void ViewMetadataAndBytesAreBorrowedWithoutAllocation()
    {
        const string Source = """
            func length(view: ref/Text.Utf8Slice) -> isize => view.length
            let text = "ab"
            let view = Text.utf8(text)
            require view.length == 2 and length(view) == 2 else => $abort("length")
            let bytes = view.bytes()
            require bytes.length == 2 and bytes[0] == 97 and bytes[1] == 98 else => $abort("bytes")
            Console.writeLine(view)
            """;
        NativeAllocationAudit.WriteFixture("Utf8BorrowMetadata", Source, 0, 0, 0, "ab\n");
    }

    [Theory]
    [InlineData("_ = storage[0]\n_ = copy", false)]
    [InlineData("_ = copy\n_ = storage[0]", true)]
    public void CopiedViewKeepsTheOriginalExclusiveLoan(string use, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Fixed + "match (buffer@move).intoText()\n    .Ok(let view)\n        let copy = view\n        " + use.Replace("\n", "\n        ", StringComparison.Ordinal) + "\n    .Err(_) => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
    }
}
