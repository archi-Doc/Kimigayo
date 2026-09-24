// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryLibraryTest
{
    [Fact]
    public void UserHelperNamesDoNotReplaceTheLibraryAlgorithms()
    {
        const string Source = """
            group DictionaryStorage
                public func initialize() => $abort("user initialize")
                public func find() => $abort("user find")
                public func unlink() => $abort("user unlink")
                public func compact() => $abort("user compact")
            var entries: Dictionary<i32, i32> = [:]
            entries.reserve(8)
            _ = entries.tryInsert(1, 10)
            _ = entries.tryInsert(2, 20)
            _ = entries.remove(1)
            entries.shrinkToFit()
            require entries[2] == 20 else => $abort("stored value")
            entries.clear()
            require entries.length == 0 else => $abort("clear")
            """;
        ScalarEmissionTest.EmitFixture("DictionaryLibraryIdentity", Source, string.Empty);
    }

    [Fact]
    public void ProgramsWithoutDictionariesDoNotCompileStorageAlgorithms()
    {
        var c = MinimalEmissionTest.Analyze("Console.writeLine(\"plain\")");
        using var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.DoesNotContain(c.Ownership.Bodies, body => body.Function.CodeContext.SourceDocument?.Path.EndsWith("DictionaryStorage.kimi", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("__kimi_dictionary_", output.ToString());
    }

    [Fact]
    public void PrivateHandleUsesTheExpectedPlatformLayout()
    {
        var c = MinimalEmissionTest.Analyze("var entries: Dictionary<i32, i32> = [:]");
        var pointer = c.Library.DictionaryUnlink.Parameters[0].Type.BoundType!;
        var layout = Assert.IsType<AggregateLayout>(new AggregateLayoutPool().Get(pointer.Components[0]));
        Assert.True(layout.CLayout);
        Assert.Equal(56, layout.Value.Layout.Size);
        Assert.Equal(8, layout.Value.Layout.Alignment);
        Assert.Equal(new[] { 0, 8, 16, 24, 32, 40, 48 }, layout.Value.Layout.FieldOffsets.ToArray());
    }

    [Fact]
    public void PointerReturningCallbacksUseOrdinaryCallAbi()
    {
        const string Source = """
            func invoke(f: (isize) -> unsafe/u8) -> unsafe/u8 => f(0)
            unsafe
                let address: usize = 4096
                let expected = address@unsafe/u8
                let concrete = func [address] (offset: isize) -> unsafe/u8
                    unsafe => return address@unsafe/u8
                let erased: (isize) -> unsafe/u8 = concrete
                require concrete(0) == expected and erased(0) == expected else => $abort("pointer result")
                require invoke(erased@move) == expected else => $abort("forwarded result")
            """;
        ScalarEmissionTest.EmitFixture("DictionaryPointerCallbacks", Source, string.Empty);
    }

    [Fact]
    public void RemovalCompilesTheOrdinaryLibrarySource()
    {
        var c = MinimalEmissionTest.Analyze("var entries: Dictionary<i32, i32> = [:]\n_ = entries.tryInsert(1, 2)\n_ = entries.remove(1)");
        using var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryUnlink));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryAppendSlot));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryInitialize));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryClearLinks));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryFind));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryClear));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryCompact));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryShrink));
        Assert.Equal(CompilerFunctionKind.None, c.Library.DictionaryUnlink.BoundSymbol!.CompilerFunction);
        Assert.Contains("DictionaryStorage.kimi", c.Library.DictionaryUnlink.CodeContext.SourceDocument!.Path);
        Assert.DoesNotContain("%left_index = sub", output.ToString());
        ScalarEmissionTest.WriteFixture("DictionaryLibraryRemoval", output.ToString(), string.Empty);
    }

    [Theory]
    [InlineData("unsafe => Kimi.DictionaryStorage.unlink(null, 24, 1)")]
    [InlineData("unsafe => Kimi.DictionaryStorage.initialize(null)")]
    [InlineData("unsafe => Kimi.DictionaryStorage.clearLinks(null)")]
    public void PrivateStorageFunctionsAreNotAPublicUnsafeAPI(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.InaccessibleBinding_Kd);
    }
}
