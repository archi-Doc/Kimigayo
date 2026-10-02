// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class GenericPointerEmissionTest
{
    [Fact]
    public void ConcreteReadsPreserveCopyMoveAndCleanup()
    {
        var source = """
            struct Resource
                public var value: i32
                drop
                    require self.value == 0 else => $abort("value")
                    Console.writeLine("drop")
            struct Empty
                drop => Console.writeLine("empty")
            enum Choice<T>
                None
                Some(T)
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: raw/u8, size: u64, kind: u32, protect: u32) -> raw/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: raw/u8, size: u64, kind: u32) -> i32
            func take<E>(pointer: raw/E) -> E
                unsafe => return (*pointer)@move
            func at<E>(pointer: raw/E, index: isize) -> E
                unsafe => return pointer[index]@move
            func reads(bytes: raw/u8)
                unsafe
                    let integer = take(bytes@raw/i32)
                    require integer == 0 else => $abort("integer")
                    require take(bytes@raw/bool) == false else => $abort("bool")
                    require take(bytes@raw/raw/i32) == null else => $abort("pointer")
                    let item = at(bytes@raw/Resource, 1)
                    let tuple = take((bytes + 32)@raw/(Resource, Resource))
                    let array = at((bytes + 64)@raw/[2 of Resource], 1)
                    let empty = take((bytes + 128)@raw/Empty)
                    let unit = take(bytes@raw/())
                    let text = take((bytes + 160)@raw/string)
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        CompilationTestHelper.WriteIr(c);
    }

    [Theory]
    [InlineData("ref/i32 during static")]
    [InlineData("(ref/i32 during static, i32)")]
    public void DependentInstantiationsPreserveTheCompletePointee(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func take<E>(pointer: raw/E) -> E\n    unsafe => return (*pointer)@move\nfunc read(pointer: raw/({type})) => take(pointer)\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        CompilationTestHelper.WriteIr(c);
    }

    [Theory]
    [InlineData("ref/i32 during static")]
    [InlineData("(ref/i32 during static, i32)")]
    public void VerifiedTemplateBuildsDependentConcreteReads(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func take<E>(pointer: raw/E) -> E\n    unsafe => return (*pointer)@move\nfunc input(pointer: raw/({type})) => ()\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var generic = c.Ownership.Bodies.Single(x => x.Function.Name == "take");
        var input = c.Ownership.Bodies.Single(x => x.Function.Name == "input");
        var element = input.Function.Parameters[0].Type.BoundType!.Components[0];
        var call = new BoundCall();
        call.Set(generic.Function.BoundSymbol!, element, null, [0], [element]);
        Assert.NotNull(c.Ownership.AnalyzeInstance(generic, call));
    }

    [Fact]
    public void AcquiredGenericValueCannotBeMovedTwice()
    {
        var c = MinimalEmissionTest.Analyze("func take<E>(pointer: raw/E) -> E\n    unsafe\n        let value = (*pointer)@move\n        let moved = value@move\n        return value@move\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.ErrorCount > 0);
    }
}
