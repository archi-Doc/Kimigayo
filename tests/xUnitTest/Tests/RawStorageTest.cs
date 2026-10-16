// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 5.6: Kimi.Raw. allocate is safe and Aborts on a negative count or an unrepresentable size; a zero-byte request returns a
// nonnull substitute that release ignores. release and initialize are unsafe; initialize moves its value in and destroys
// nothing, and placed values leave the storage only through raw Place Take or relocation (SPEC 5.2).
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

    // SPEC 5.6: slice forms a shared Slice over initialized storage; its result-only Origin is fixed by the caller's expected Type,
    // here the receiver's, and a zero length may use any storage, including null.
    [Fact]
    public void SlicesViewInitializedStorage()
    {
        var source = """
            struct Holder
                let data: raw/i32
                let length: isize
                public init(length: isize)
                    self.data = Raw.allocate<i32>(length)
                    self.length = length
                    var i: isize = 0
                    while i < length
                        unsafe => Raw.initialize(self.data + i, (i * 10)@i32)
                        i += 1
                public func items(self: ref/Self) -> Slice<i32>
                    unsafe => return Raw.slice(self.data, self.length)
                drop
                    unsafe => Raw.release(self.data)
            func emptySlice() -> Slice<i32>
                unsafe => return Raw.slice(null@raw/i32, 0)
            public func main()
                let holder = Holder.init(4)
                var total: i32 = 0
                for item in holder.items()
                    total += item
                let view = holder.items()
                require total == 60 and view.length == 4 and view[3] == 30 else => $abort("view")
                require emptySlice().length == 0 else => $abort("empty")
                Console.writeLine("sliced")
            """;
        ScalarEmissionTest.EmitFixture("RawStorageSlice", source, "sliced\n");
    }

    // SPEC 3.4, 5.2: a value placed in raw storage occupies its own Place, so zero-sized values at one address are placed,
    // taken and destroyed one by one, also by an owning iterator that stops early, and a component of a placed value is
    // taken alone before release.
    [Fact]
    public void ZeroSizedValuesOccupyTheirOwnPlaces()
    {
        var source = """
            struct Token
                drop => Console.writeLine("drop")
            struct Buffer<T>
                let data: raw/T
                var length: isize
                let capacity: isize
                public init(capacity: isize)
                    self.data = Raw.allocate<T>(capacity)
                    self.length = 0
                    self.capacity = capacity
                public func push(self: uniq/Self, value: T)
                    require self.length < self.capacity else => $abort("full")
                    unsafe => Raw.initialize(self.data + self.length, value@move)
                    self.length += 1
                drop
                    let data = self.data
                    while self.length > 0
                        self.length -= 1
                        unsafe => _ = data[self.length]@move
                    unsafe => Raw.release(data)
            func buffer()
                var tokens = Buffer<Token>.init(capacity: 2)
                tokens.push(Token.init())
                tokens.push(Token.init())
                Console.writeLine("buffer")
            func owning()
                let values: Array<Token> = [Token.init(), Token.init(), Token.init()]
                for item in values@move
                    Console.writeLine("item")
                    exit
            func component()
                let p = Raw.allocate<(i64, Token)>(1)
                unsafe
                    Raw.initialize(p, (7, Token.init()))
                    _ = (*p).1@move
                    require (*p).0 == 7 else => $abort("component")
                    Raw.release(p)
                Console.writeLine("component")
            public func main()
                buffer()
                owning()
                component()
            """;
        ScalarEmissionTest.EmitFixture(
            "RawStorageZeroSizedPlaces", source, "buffer\ndrop\ndrop\nitem\ndrop\ndrop\ndrop\ndrop\ncomponent\n");
    }

    [Theory]
    [InlineData("Negative", "-1", "abort KIMI_E_ARG_RANGE: Argument out of range")]
    [InlineData("Oversized", "4611686018427387904", "abort KIMI_E_ALLOC_SIZE: Allocation size exceeds limit")]
    public void InvalidCountsAbort(string name, string count, string message)
        => ScalarEmissionTest.EmitFixture("RawStorageCount" + name, "let count: isize = " + count + "\nlet storage = Raw.allocate<i64>(count)\nConsole.writeLine(\"after\")", string.Empty, 1, "Hello.kimi:2:15: " + message + "\n");

    [Theory]
    [InlineData("let storage = Raw.allocate<i32>(1)", true)]
    [InlineData("Raw.release(null@raw/i32)", false)]
    [InlineData("Raw.initialize(null@raw/i32, 1)", false)]
    [InlineData("let view: Slice<i32> = Raw.slice(null@raw/i32, 0)", false)]
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

    // SPEC 5.6, 8.11.2: allocate is an allocation and release and initialize are raw accesses; none is an environment effect.
    [Fact]
    public void RawOperationsAreNoEnvironmentEffects()
    {
        const string Source = "contract Sink\n    func put(self: uniq/Self, value: i32)\n        effect confined\n" +
            "struct RawSink\n    Self is Sink\n    public func put(self: uniq/Self, value: i32)\n        let storage = Raw.allocate<i32>(1)\n" +
            "        unsafe\n            Raw.initialize(storage, value)\n            Raw.release(storage)\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
