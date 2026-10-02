// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 5.6: Kimi.Raw. allocate is safe and Aborts on a negative count or an unrepresentable size; a zero-byte request returns a
// nonnull substitute that release ignores. release and initialize are unsafe; initialize moves its value in and destroys
// nothing, and elements leave the storage only through raw Place Take.
public class RawStorageTest
{
    private const string Resource = "struct Resource\n    public var value: i32\n    public init(value: i32) => self.value = value\n    drop => Console.writeLine(\"drop\")\n";

    [Fact]
    public void StorageIsAllocatedInitializedTakenAndReleased()
    {
        var source = Resource + """
            struct Pair
                public var text: string
                public var number: i64
                public init(text: string, number: i64)
                    self.text = text@move
                    self.number = number
            public func main()
                let storage = Raw.allocate<Resource>(2)
                unsafe
                    Raw.initialize(storage, Resource.init(1))
                    Raw.initialize(storage + 1, Resource.init(2))
                    require (*storage).value == 1 and storage[1].value == 2 else => $abort("values")
                    let first = (*storage)@move
                    _ = storage[1]@move
                    Raw.release(storage)
                    Console.writeLine("released")
                let pairs = Raw.allocate<Pair>(1)
                let texts = Raw.allocate<string>(1)
                unsafe
                    Raw.initialize(pairs, Pair.init("pair", 7))
                    Raw.initialize(texts, "text")
                    let pair = (*pairs)@move
                    let text = (*texts)@move
                    require pair.text == "pair" and pair.number == 7 and text == "text" else => $abort("aggregate")
                    Raw.release(pairs)
                    Raw.release(texts)
                let units = Raw.allocate<()>(5)
                let none = Raw.allocate<i32>(0)
                require units != null and none != null else => $abort("substitute")
                unsafe
                    Raw.release(units)
                    Raw.release(none)
                    Raw.release(null@raw/i32)
                Console.writeLine("done")
            """;
        ScalarEmissionTest.EmitFixture("RawStorageLifecycle", source, "drop\nreleased\ndrop\ndone\n");
    }

    [Theory]
    [InlineData("Negative", "-1", "abort KIMI_E_ARGUMENT: Invalid argument value")]
    [InlineData("Oversized", "4611686018427387904", "abort KIMI_E_ALLOC_SIZE: Allocation size exceeds limit")]
    public void InvalidCountsAbort(string name, string count, string message)
        => ScalarEmissionTest.EmitFixture("RawStorageCount" + name, "let count: isize = " + count + "\nlet storage = Raw.allocate<i64>(count)\nConsole.writeLine(\"after\")", string.Empty, 1, "Hello.kimi:2:15: " + message + "\n");

    [Theory]
    [InlineData("let storage = Raw.allocate<i32>(1)", true)]
    [InlineData("Raw.release(null@raw/i32)", false)]
    [InlineData("Raw.initialize(null@raw/i32, 1)", false)]
    public void OnlyReleaseAndInitializeNeedAnUnsafeContext(string statement, bool valid)
    {
        var diagnostics = DiagnosticCorpus.Check("public func main()\n    " + statement + "\n").Diagnostics;
        if (valid)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            Assert.Equal(nameof(DiagnosticCode.UnsafeBlockRequired_Kd), Assert.Single(diagnostics).Code);
        }
    }

    // SPEC 5.6, 8.4.10.2: allocate is an allocation and release and initialize are raw accesses; none is an environment effect.
    [Fact]
    public void RawOperationsAreNoEnvironmentEffects()
    {
        const string Source = "contract Sink\n    func put(self: uniq/Self, value: i32)\n        effect confined\n" +
            "struct RawSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32)\n        let storage = Raw.allocate<i32>(1)\n" +
            "        unsafe\n            Raw.initialize(storage, value)\n            Raw.release(storage)\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(KimiDeclarationId.RawAllocate)]
    [InlineData(KimiDeclarationId.RawRelease)]
    [InlineData(KimiDeclarationId.RawInitialize)]
    public void OperationsKeepTheirOwnElementType(KimiDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        var function = (Kimi.Compiler.Parsing.FunctionKoto)symbol.Declaration;
        var parameter = function.Parameters[^1];
        parameter.Type.BoundType = id == KimiDeclarationId.RawAllocate ? BoundType.Primitives["i32"] : BoundType.ISize;
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(function, c.Library.InvalidDeclaration);
    }
}
