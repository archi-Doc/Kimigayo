// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// Abi.Of and Abi.Matches (IMPL 21.3.1, SPEC 22.6; R5 P8, DECISIONS R5-M58): the internal convention's order, kinds, zero-sized
// omission, result slot and flags; no gate; #LibraryImport's C conventions, Windows x64's and System V x86-64's (IMPL 21.2: one set of
// C rules, and System V's extension of 8- and 16-bit integers by signedness; the Tatara plan's B7b2b).
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
    [InlineData("i32", (int)Abi.Flags.OutOfLine, "i32 (" + Logical + ") noinline")]
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
        Assert.False(Abi.Matches(abi, c.Layout, Abi.Convention.Internal, ErasedFlags | Abi.Flags.OutOfLine, i32, parameters));
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
        var layout = CompilationTestHelper.BindSuccess("()").Layout;
        var i32 = BoundType.Primitives["i32"];
        var abi = Abi.Of(layout, Abi.Convention.Internal, Abi.Flags.None, BoundType.Never, [BoundType.Never, i32]);
        Assert.Equal("void (i32 a1 Value 1) noreturn", Describe(abi));
        Assert.True(Abi.Matches(abi, layout, Abi.Convention.Internal, Abi.Flags.None, BoundType.Never, [BoundType.Never, i32]));
    }

    [Theory]
    [InlineData((int)Abi.Convention.C, "void (ptr a0 Value 0, ptr a1 Value 1, i8 a2 Value 2)")]
    [InlineData((int)Abi.Convention.SystemV, "void (ptr a0 Value 0, ptr a1 Value 1, i8 zeroext a2 Value 2)")]
    public void TheCConventionPassesEveryParameterAsAValue(int code, string expected)
    {
        // SPEC 22.6: a raw pointer and a borrow pass an address, a scalar its value; nothing is a slot, and a Unit result is void; under
        // System V the u8 is zero-extended.
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.NativeRequirements[TargetProfile.Windows.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Import));
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        var import = Assert.Single(c.Binding.LibraryImports, x => x.Decl.Declaration.CodeContext.Kotonoha == c.Kotonoha);
        var parameters = Parameters(c, "touch");
        var convention = (Abi.Convention)code;
        var abi = Abi.Of(c.Layout, convention, Abi.Flags.None, import.Decl.Type!, parameters);
        Assert.Equal(expected, Describe(abi));
        Assert.True(Abi.Matches(abi.Named("touch"), c.Layout, convention, Abi.Flags.None, BoundType.Unit, parameters));
        Assert.False(Abi.Matches(abi, c.Layout, convention, Abi.Flags.None, BoundType.Primitives["i32"], parameters));
    }

    // SPEC 22.6: a C signature takes no flags and passes every parameter as a value, so a flag or a parameter without an argument value is a
    // broken contract (Binding admits neither), never a silent omission; System V's convention is a C convention.
    [Theory]
    [InlineData((int)Abi.Convention.C)]
    [InlineData((int)Abi.Convention.SystemV)]
    public void TheCConventionRefusesFlagsAndOmissions(int code)
    {
        var convention = (Abi.Convention)code;
        var layout = CompilationTestHelper.BindSuccess("()").Layout;
        var i32 = BoundType.Primitives["i32"];
        Assert.Throws<InvalidOperationException>(() => Abi.Of(layout, convention, Abi.Flags.CallerLocation, i32, [i32]));
        Assert.Throws<InvalidOperationException>(() => Abi.Of(layout, convention, Abi.Flags.None, i32, [BoundType.Unit, i32]));
        var abi = Abi.Of(layout, convention, Abi.Flags.None, i32, [i32]);
        Assert.Throws<InvalidOperationException>(() => Abi.Matches(abi, layout, convention, Abi.Flags.ResultSlot, i32, [i32]));
    }

    // IMPL 21.2 (the Tatara plan's B7b2b): under System V an 8- or 16-bit integer parameter and result carry their extension by signedness
    // and every other Type none; Windows x64's C writes none, so the two conventions' signatures match exactly where System V extends
    // nothing.
    [Theory]
    [InlineData("i8", " signext")]
    [InlineData("i16", " signext")]
    [InlineData("u8", " zeroext")]
    [InlineData("u16", " zeroext")]
    [InlineData("i32", "")]
    [InlineData("u64", "")]
    [InlineData("f32", "")]
    public void TheSystemVConventionExtendsNarrowIntegersBySignedness(string name, string extension)
    {
        var layout = CompilationTestHelper.BindSuccess("()").Layout;
        var type = BoundType.Primitives[name];
        var abi = Abi.Of(layout, Abi.Convention.SystemV, Abi.Flags.None, type, [type]);
        Assert.Equal(extension, abi.ResultExtension);
        Assert.Equal(extension, Assert.Single(abi.Parameters).Extension);
        Assert.Equal(extension, abi.Named("f").ResultExtension);
        Assert.True(Abi.Matches(abi.Named("f"), layout, Abi.Convention.SystemV, Abi.Flags.None, type, [type]));
        var windows = Abi.Of(layout, Abi.Convention.C, Abi.Flags.None, type, [type]);
        Assert.Equal(abi.Result + " (" + abi.Parameters[0].Type + " a0 Value 0)", Describe(windows));
        Assert.Equal(extension.Length == 0, Abi.Matches(windows, layout, Abi.Convention.SystemV, Abi.Flags.None, type, [type]));
        Assert.Equal(extension.Length == 0, Abi.Matches(abi, layout, Abi.Convention.C, Abi.Flags.None, type, [type]));
    }

    // IMPL 21.2 (the owner's O-504 Q1 (a)): Matches compares the result's and each parameter's extension, so i16 and u16, which share
    // their argument Type, give signatures that do not match under System V.
    [Fact]
    public void MatchesComparesTheExtensions()
    {
        var layout = CompilationTestHelper.BindSuccess("()").Layout;
        var (i16, u16, i32) = (BoundType.Primitives["i16"], BoundType.Primitives["u16"], BoundType.Primitives["i32"]);
        Assert.False(Abi.Matches(Abi.Of(layout, Abi.Convention.C, Abi.Flags.None, u16, [i32]), layout, Abi.Convention.SystemV, Abi.Flags.None, u16, [i32]));
        Assert.False(Abi.Matches(Abi.Of(layout, Abi.Convention.C, Abi.Flags.None, i32, [u16]), layout, Abi.Convention.SystemV, Abi.Flags.None, i32, [u16]));
        var signed = Abi.Of(layout, Abi.Convention.SystemV, Abi.Flags.None, i16, [i16]);
        Assert.Equal("i16 signext (i16 signext a0 Value 0)", Describe(signed));
        Assert.Equal("i16 zeroext (i16 zeroext a0 Value 0)", Describe(Abi.Of(layout, Abi.Convention.SystemV, Abi.Flags.None, u16, [u16])));
        Assert.False(Abi.Matches(signed, layout, Abi.Convention.SystemV, Abi.Flags.None, u16, [i16]));
        Assert.False(Abi.Matches(signed, layout, Abi.Convention.SystemV, Abi.Flags.None, i16, [u16]));
    }

    // IMPL 21.3.1 with SPEC 22.4.5: a program-supplied platform's members under the internal convention, in Platform's requirement order:
    // start's Self is a slot Type returned through the result slot (none where it is zero-sized) and its Boot an owned slot; every receiver
    // `uniq/Self` a pointer value, a zero-sized platform's too; write's Text.Utf8Slice an owned slot; terminate's Never no return.
    [Theory]
    [InlineData(
        false,
        "void (ptr ret ResultSlot -1, ptr a0 OwnedSlot 0) slot",
        "ptr (ptr a0 Value 0, i64 a1 Value 1)",
        "i64 (ptr a0 Value 0, ptr a1 Value 1, i64 a2 Value 2)",
        "i64 (ptr a0 Value 0, i32 a1 Value 1, ptr a2 OwnedSlot 2)",
        "i64 (ptr a0 Value 0)",
        "void (ptr a0 Value 0, i32 a1 Value 1) noreturn")]
    [InlineData(
        true,
        "void (ptr a0 OwnedSlot 0)",
        "ptr (ptr a0 Value 0, i64 a1 Value 1)",
        "i64 (ptr a0 Value 0, ptr a1 Value 1, i64 a2 Value 2)",
        "i64 (ptr a0 Value 0, i32 a1 Value 1, ptr a2 OwnedSlot 2)",
        "i64 (ptr a0 Value 0)",
        "void (ptr a0 Value 0, i32 a1 Value 1) noreturn")]
    public void APlatformsMembersTakeTheInternalConvention(bool fieldless, params string[] expected)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.None.Target));
        var board = fieldless ? KernelSources.FieldlessBoard : KernelSources.Board + KernelSources.BoardTerminate;
        c.Kotonoha.AddSource(new SourceDocument("Kernel.kimi", KernelSources.Hello + board));
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete, string.Join('\n', c.Binding.StartupIssues));
        var described = new List<string>();
        foreach (var witness in c.Binding.PlatformWitnesses)
        {
            BoundType[] parameters = [.. ((FunctionKoto)witness.Declaration).Parameters.Select(static x => x.Type.TypeOf()!)];
            described.Add(Describe(Abi.Of(c.Layout, Abi.Convention.Internal, Abi.Flags.None, witness.Type!, parameters)));
        }

        Assert.Equal(expected, described);
    }

    private static BoundType[] Parameters(Compilation c, string name)
        => [.. KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name).Parameters.Select(static x => x.Type.TypeOf()!)];

    private static string Describe(FunctionAbi abi)
        => abi.Result + abi.ResultExtension + " (" + string.Join(", ", abi.Parameters.Select(static x => $"{x.Type}{x.Extension} {x.Name} {x.Kind} {x.LogicalIndex}")) + ")" +
            (abi.ResultSlot ? " slot" : string.Empty) + (abi.CallerLocation ? " location" : string.Empty) + (abi.NoReturn ? " noreturn" : string.Empty) +
            (abi.OutOfLine ? " noinline" : string.Empty);
}
