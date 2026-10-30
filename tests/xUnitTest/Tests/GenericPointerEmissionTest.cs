// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class GenericPointerEmissionTest
{
    [Fact]
    public void ConcreteReadsPreserveCopyMoveAndCleanup()
    {
        var source = $$"""
            struct Resource
                public var value: i32 = 0
                drop
                    require self.value == 0 else => $abort("value")
                    Console.writeLine("drop")
            struct Empty
                drop => Console.writeLine("empty")
            enum Choice<T>
                None
                Some(T)
            {{ForeignEmissionTest.Pages()}}
            func take<E>(pointer: raw/E) -> E
                unsafe => return (*pointer)@move
            func at<E>(pointer: raw/E, index: isize) -> E
                unsafe => return pointer[index]@move
            func reads(bytes: raw/u8)
                unsafe
                    // Each value is placed, then taken once; a Take leaves its Place holding no value for the next.
                    Raw.initialize(bytes@raw/i32, 0)
                    let integer = take(bytes@raw/i32)
                    require integer == 0 else => $abort("integer")
                    Raw.initialize(bytes@raw/bool, false)
                    require take(bytes@raw/bool) == false else => $abort("bool")
                    Raw.initialize(bytes@raw/raw/i32, null)
                    require take(bytes@raw/raw/i32) == null else => $abort("pointer")
                    Raw.initialize(bytes@raw/Resource + 1, Resource.init())
                    let item = at(bytes@raw/Resource, 1)
                    Raw.initialize((bytes + 32)@raw/(Resource, Resource), (Resource.init(), Resource.init()))
                    let tuple = take((bytes + 32)@raw/(Resource, Resource))
                    Raw.initialize((bytes + 64)@raw/[2 of Resource] + 1, [Resource.init(), Resource.init()])
                    let array = at((bytes + 64)@raw/[2 of Resource], 1)
                    Raw.initialize((bytes + 128)@raw/Empty, Empty.init())
                    let empty = take((bytes + 128)@raw/Empty)
                    Raw.initialize(bytes@raw/(), ())
                    let unit = take(bytes@raw/())
                    Raw.initialize((bytes + 160)@raw/string, "")
                    let text = take((bytes + 160)@raw/string)
                    Raw.initialize((bytes + 224)@raw/Choice<Resource>, Choice<Resource>.None)
                    let choice = take((bytes + 224)@raw/Choice<Resource>)
                    Console.writeLine("scope")
            public func main()
                unsafe
                    let bytes = Native.allocate(null, 4096, 12288, 4)
                    require bytes != null else => $abort("allocate")
                    reads(bytes)
                    require Native.free(bytes, 0, 32768) != 0 else => $abort("free")
                Console.writeLine("done")
            """;
        ScalarEmissionTest.EmitFixture("GenericPointerRead", source, "scope\nempty\ndrop\ndrop\ndrop\ndrop\ndrop\ndone\n");
    }

    [Theory]
    [InlineData("(*pointer)@move")]
    [InlineData("pointer[index]@move")]
    public void AbstractOwnedPointeeCanBeAcquired(string expression)
    {
        var c = MinimalEmissionTest.Analyze($"func take<E>(pointer: raw/E, index: isize) -> E\n    unsafe => return {expression}\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        CompilationTestHelper.WriteIr(c);
    }

    [Theory]
    [InlineData("ref/i32 during static")]
    [InlineData("(ref/i32 during static, i32)")]
    public void DependentInstantiationsPreserveTheCompletePointee(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func take<E>(pointer: raw/E) -> E\n    unsafe => return (*pointer)@move\nfunc read(pointer: raw/({type})) => take(pointer)\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        CompilationTestHelper.WriteIr(c);
    }

    [Fact]
    public void AcquiredGenericValueCannotBeMovedTwice()
    {
        var c = MinimalEmissionTest.Analyze("func take<E>(pointer: raw/E) -> E\n    unsafe\n        let value = (*pointer)@move\n        let moved = value@move\n        return value@move\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.ErrorCount > 0);
    }
}
