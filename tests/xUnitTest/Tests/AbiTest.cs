// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// Abi.Of and Abi.Matches (IMPL 21.3.1, SPEC 22.6; R5 P8, DECISIONS R5-M58): the internal convention's order, kinds, zero-sized
// omission, result slot and flags; no gate; #LibraryImport's C convention.
public class AbiTest
{
    private const string Probe = "func probe(a: i32, b: (), c: string, d: (i32, i64), e: ref/string) -> i32 => a\n";
    private const string Logical = "i32 a0 Value 0, ptr a2 OwnedSlot 2, ptr a3 OwnedSlot 3, ptr a4 SharedReference 4";
    private const string Erased = "i32 (i64 environment Environment -1, " + Logical + ", ptr context Context -1, " +
        "ptr location Location -1, i64 location_length LocationLength -1) location";

    private const string Import = "group Native\n    #LibraryImport(\"codec\", \"touch\")\n" +
        "    public unsafe func touch(pointer: raw/i32, value: uniq/i64, small: u8) -> ()\npublic func main() => ()";

    private const string Concrete = "void (ptr environment Environment -1, ptr ret ResultSlot -1, " + Logical + ", ptr context Context -1) slot";

    [Theory]
    [InlineData("i32", (int)Abi.Flags.None, "i32 (" + Logical + ")")]
    [InlineData("(i32, i64)", (int)Abi.Flags.None, "void (ptr ret ResultSlot -1, " + Logical + ") slot")]
    [InlineData("()", (int)Abi.Flags.ResultSlot, "void (ptr ret ResultSlot -1, " + Logical + ") slot")]
    [InlineData("i32", (int)(Abi.Flags.ErasedClosure | Abi.Flags.CallerLocation), Erased)]
    [InlineData("(i32, i64)", (int)Abi.Flags.ConcreteClosure, Concrete)]
    public void TheInternalConventionOrdersKindsAndOmits(string result, int flags, string expected)
    {
        // IMPL 21.3.1: [environment] [ret] a0..an [context] [location, location_length]; a string is passed by its owned slot, a slot
        // Type by pointer, a string borrow as a shared reference, and the zero-sized `b` has no slot.
        var c = CompilationTestHelper.BindSuccess(Probe + "()");
        var parameters = Parameters(c, "probe");
        var type = result switch { "i32" => BoundType.Primitives["i32"], "()" => BoundType.Unit, _ => parameters[3] };
        var abi = Abi.Of(c.Layout, Abi.Convention.Internal, (Abi.Flags)flags, type, parameters);
        Assert.Equal(expected, Describe(abi));
        Assert.Equal(string.Empty, abi.Name);
        Assert.True(Abi.Matches(abi.Named("probe"), c.Layout, Abi.Convention.Internal, (Abi.Flags)flags, type, parameters));
    }

    [Fact]
    public void MatchesComparesEveryInputButTheName()
    {
        var c = CompilationTestHelper.BindSuccess(Probe + "()");
        var parameters = Parameters(c, "probe");
        var i32 = BoundType.Primitives["i32"];
        const Abi.Flags ErasedFlags = Abi.Flags.ErasedClosure | Abi.Flags.CallerLocation;
        var abi = Abi.Of(c.Layout, Abi.Convention.Internal, ErasedFlags, i32, parameters).Named("__kimi_f0");
        Assert.True(Abi.Matches(abi, c.Layout, Abi.Convention.Internal, ErasedFlags, i32, parameters));
        Assert.False(Abi.Matches(abi, c.Layout, Abi.Convention.Internal, Abi.Flags.ConcreteClosure | Abi.Flags.CallerLocation, i32, parameters));
        Assert.False(Abi.Matches(abi, c.Layout, Abi.Convention.Internal, Abi.Flags.ErasedClosure, i32, parameters));
        Assert.False(Abi.Matches(abi, c.Layout, Abi.Convention.Internal, ErasedFlags | Abi.Flags.ResultSlot, i32, parameters));
        Assert.False(Abi.Matches(abi, c.Layout, Abi.Convention.Internal, ErasedFlags, BoundType.Primitives["i64"], parameters));
        Assert.False(Abi.Matches(abi, c.Layout, Abi.Convention.Internal, ErasedFlags, i32, parameters.AsSpan(0, 4)));
        Assert.False(Abi.Matches(abi, c.Layout, Abi.Convention.Internal, ErasedFlags, i32, [parameters[1], .. parameters]));

        // A zero-sized parameter has no slot, so only the logical index of the parameters after it tells the signatures apart.
        Assert.True(Abi.Matches(abi, c.Layout, Abi.Convention.Internal, ErasedFlags, i32, [parameters[0], BoundType.Never, .. parameters.AsSpan(2)]));
    }

    [Fact]
    public void AbiHasNoGate()
    {
        // IMPL 21.1.3: Never is zero-sized, so a Never parameter has no slot, and a Never result returns void and does not return.
        var layout = new Layout();
        var i32 = BoundType.Primitives["i32"];
        var abi = Abi.Of(layout, Abi.Convention.Internal, Abi.Flags.None, BoundType.Never, [BoundType.Never, i32]);
        Assert.Equal("void (i32 a1 Value 1) noreturn", Describe(abi));
        Assert.True(Abi.Matches(abi, layout, Abi.Convention.Internal, Abi.Flags.None, BoundType.Never, [BoundType.Never, i32]));
    }

    [Fact]
    public void TheCConventionPassesEveryParameterAsAValue()
    {
        // SPEC 22.6: a raw pointer and a borrow pass an address, a scalar its value; nothing is a slot, and a Unit result is void.
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Import));
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        var import = Assert.Single(c.Binding.LibraryImports, x => x.Decl.Declaration.CodeContext.Kotonoha == c.Kotonoha);
        var parameters = Parameters(c, "touch");
        var abi = Abi.Of(c.Layout, Abi.Convention.C, Abi.Flags.None, import.Decl.Type!, parameters);
        Assert.Equal("void (ptr a0 Value 0, ptr a1 Value 1, i8 a2 Value 2)", Describe(abi));
        Assert.True(Abi.Matches(abi.Named("touch"), c.Layout, Abi.Convention.C, Abi.Flags.None, BoundType.Unit, parameters));
        Assert.False(Abi.Matches(abi, c.Layout, Abi.Convention.C, Abi.Flags.None, BoundType.Primitives["i32"], parameters));
    }

    // SPEC 22.6: a C signature takes no flags and passes every parameter as a value, so a flag or a parameter without an argument value is a
    // broken contract (Binding admits neither), never a silent omission.
    [Fact]
    public void TheCConventionRefusesFlagsAndOmissions()
    {
        var layout = new Layout();
        var i32 = BoundType.Primitives["i32"];
        Assert.Throws<InvalidOperationException>(() => Abi.Of(layout, Abi.Convention.C, Abi.Flags.CallerLocation, i32, [i32]));
        Assert.Throws<InvalidOperationException>(() => Abi.Of(layout, Abi.Convention.C, Abi.Flags.None, i32, [BoundType.Unit, i32]));
        var abi = Abi.Of(layout, Abi.Convention.C, Abi.Flags.None, i32, [i32]);
        Assert.Throws<InvalidOperationException>(() => Abi.Matches(abi, layout, Abi.Convention.C, Abi.Flags.ResultSlot, i32, [i32]));
    }

    private static BoundType[] Parameters(Compilation c, string name)
        => [.. KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name).Parameters.Select(static x => x.Type.TypeOf()!)];

    private static string Describe(FunctionAbi abi)
        => abi.Result + " (" + string.Join(", ", abi.Parameters.Select(static x => $"{x.Type} {x.Name} {x.Kind} {x.LogicalIndex}")) + ")" +
            (abi.ResultSlot ? " slot" : string.Empty) + (abi.CallerLocation ? " location" : string.Empty) + (abi.NoReturn ? " noreturn" : string.Empty);
}
