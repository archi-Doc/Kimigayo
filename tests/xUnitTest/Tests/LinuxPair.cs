// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers.Binary;
using System.Text;
using Kimi;
using Kimi.Build;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The Tatara plan's B7b7: the shared reading of a static ELF executable (ElfFile, moved from TataraExecutorTest) and the Linux pair
// helper the native build's ELF rows use (NativeToolchainTest; NamedProvider's L rows reuse it): a Linux-prepared compilation published
// by EmissionArtifacts.Publish with explicit paths and built by NativeToolchain.BuildManifest, the build --Manifest path; its C libraries
// compiled by the pinned clang for x86_64-unknown-linux-musl and archived by a copy of the toolchain's llvm-lib.exe named llvm-ar.exe
// (one LLVM binary that dispatches on its name; rulings O9 and 7 (a)), never written into the toolchain.
internal static class LinuxPair
{
    // The C flags of a library member (Archive): no builtins, no stack protector, Microsoft's extensions for selectany.
    private static readonly string[] Flags = ["--target=" + TargetProfile.Linux.Target, "-O2", "-fno-builtin", "-fno-stack-protector", "-fms-extensions", "-c"];

    /// <summary>Publishes a Linux Application pair of the source into directory/out/Hello.ll at the level, its program libraries under
    /// the Linux key (each <c>native/&lt;input&gt;</c>, static unless the kind says otherwise).</summary>
    /// <param name="directory">The project's directory.</param>
    /// <param name="source">main.kimi's text.</param>
    /// <param name="level">O0 or O2.</param>
    /// <param name="libraries">Each program library's name, Kind and input file under native/.</param>
    /// <returns>The pair's paths.</returns>
    internal static ArtifactPaths Publish(string directory, string source, string level, params (string Name, string Kind, string Input)[] libraries)
    {
        var c = Compilation.CreateForTest();
        var settings = c.Project.ProjectFile;
        settings.NativeLibraries[TargetProfile.Linux.Target] = new();
        foreach (var (name, kind, input) in libraries)
        {
            settings.NativeLibraries[TargetProfile.Linux.Target][name] = new() { Kind = kind, Input = "native/" + input };
        }

        settings.Optimization = level;
        Assert.True(c.Prepare(TargetProfile.Linux.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        c.Project.Directory = directory;
        c.Project.Name = "Hello";
        var paths = ArtifactPaths.Create(Path.Combine(directory, "out", "Hello.ll"), level, TargetProfile.Linux);
        Assert.True(EmissionArtifacts.Publish(c, paths), MinimalEmissionTest.Describe(c));
        return paths;
    }

    /// <summary>Builds a pair as build --Manifest does.</summary>
    /// <param name="paths">The pair's paths.</param>
    /// <returns>The build.</returns>
    internal static Task Build(ArtifactPaths paths) => NativeToolchain.BuildManifest(paths.Manifest, (_, _) => { }, TestContext.Current.CancellationToken);

    /// <summary>Builds directory/native/&lt;library&gt;.a: each member's C source compiled by the pinned clang for the Linux triple and
    /// archived by the llvm-ar copy in command-line order, which its index keeps (an llvm-ar index lists definers in member order).</summary>
    /// <param name="directory">The project's directory.</param>
    /// <param name="library">The archive's name.</param>
    /// <param name="operation">llvm-ar's operation: rcs (indexed), rcS (no index) or rcsT (thin).</param>
    /// <param name="members">Each member's name and C source.</param>
    /// <returns>The archive's path.</returns>
    internal static async Task<string> Archive(string directory, string library, string operation, params (string Member, string Source)[] members)
    {
        var folder = Directory.CreateDirectory(Path.Combine(directory, "native", library + ".members")).FullName;
        foreach (var (member, text) in members)
        {
            File.WriteAllText(Path.Combine(folder, member + ".c"), text);
            Assert.Equal(string.Empty, await TataraExecutorTest.Tool("clang", folder, [.. Flags, member + ".c", "-o", member + ".o"]));
        }

        var archive = Path.Combine(directory, "native", library + ".a");
        Assert.Equal(string.Empty, await TataraExecutorTest.Tool(LlvmAr(folder), folder, [operation, archive, .. members.Select(static x => x.Member + ".o")]));
        return archive;
    }

    /// <summary>Links directory/native/&lt;library&gt;.a as a shared object of the source (a linked image; LinkAmbiguity row 22's L
    /// half, Kind's row 22): a -fPIC object linked by Tatara's ld.lld -shared, its symbol table kept.</summary>
    /// <param name="directory">The project's directory.</param>
    /// <param name="library">The image's library name.</param>
    /// <param name="source">Its C source.</param>
    /// <returns>The image's path.</returns>
    internal static Task<string> SharedObject(string directory, string library, string source) =>
        Image(Directory.CreateDirectory(Path.Combine(directory, "native")).FullName, library, ".a", source);

    /// <summary>Builds directory/native/&lt;library&gt;.a as an archive whose one member is a shared object of the source (C-544 C2: a
    /// linked image as an archive member), archived by the llvm-ar copy (rcs).</summary>
    /// <param name="directory">The project's directory.</param>
    /// <param name="library">The archive's name.</param>
    /// <param name="source">The member's C source.</param>
    /// <returns>The archive's path.</returns>
    internal static async Task<string> ImageArchive(string directory, string library, string source)
    {
        var folder = Directory.CreateDirectory(Path.Combine(directory, "native", library + ".members")).FullName;
        await Image(folder, library, ".so", source);
        var archive = Path.Combine(directory, "native", library + ".a");
        Assert.Equal(string.Empty, await TataraExecutorTest.Tool(LlvmAr(folder), folder, ["rcs", archive, library + ".so"]));
        return archive;
    }

    // folder/<stem><extension>: the source's -fPIC object linked by Tatara's ld.lld -shared, its symbol table kept.
    private static async Task<string> Image(string folder, string stem, string extension, string source)
    {
        File.WriteAllText(Path.Combine(folder, stem + ".c"), source);
        Assert.Equal(string.Empty, await TataraExecutorTest.Tool("clang", folder, [.. Flags, "-fPIC", stem + ".c", "-o", stem + ".o"]));
        var image = Path.Combine(folder, stem + extension);
        Tatara.Instance.Link(TataraExecutorTest.LinuxContext, ["ld.lld", "-m", "elf_x86_64", "-shared", "-o", image, Path.Combine(folder, stem + ".o")]);
        return image;
    }

    // The llvm-ar copy in the folder (rulings O9 and 7 (a)): the toolchain's llvm-lib.exe under llvm-ar's name; the tool's rooted stem.
    private static string LlvmAr(string folder)
    {
        var copy = Path.Combine(folder, "llvm-ar.exe");
        if (!File.Exists(copy))
        {
            File.Copy(Path.Combine(Repository.Root, "toolchain", "llvm-lib.exe"), copy);
        }

        return Path.Combine(folder, "llvm-ar");
    }
}

// §6.1's managed reading of a static ELF executable (Tatara B7b1; shared since B7b7): ELF64 little-endian; the header's type, machine and
// entry, each program header's type and flags, and .symtab's symbols with their values.
internal sealed record ElfFile(int Type, int Machine, ulong Entry, (uint Type, uint Flags)[] Segments, Dictionary<string, ulong> Symbols)
{
    internal static ElfFile Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan(0, 6).SequenceEqual("\u007fELF\u0002\u0001"u8), path + " is not an ELF64 little-endian file.");
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
        uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        ulong U64(int at) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at));
        var (programs, programSize, sections, sectionSize) = ((int)U64(32), U16(54), (int)U64(40), U16(58));
        var segments = Enumerable.Range(0, U16(56)).Select(i => (U32(programs + (i * programSize)), U32(programs + (i * programSize) + 4))).ToArray();
        var symbols = new Dictionary<string, ulong>();
        for (var header = sections; header < sections + (U16(60) * sectionSize); header += sectionSize)
        {
            // SHT_SYMTAB: its entries after the null symbol; sh_link names its string table's header.
            if (U32(header + 4) == 2)
            {
                var (start, end, size) = ((int)U64(header + 24), (int)(U64(header + 24) + U64(header + 32)), (int)U64(header + 56));
                var names = (int)U64(sections + ((int)U32(header + 40) * sectionSize) + 24);
                for (var symbol = start + size; symbol < end; symbol += size)
                {
                    var name = names + (int)U32(symbol);
                    symbols.TryAdd(Encoding.UTF8.GetString(bytes, name, Array.IndexOf(bytes, (byte)0, name) - name), U64(symbol + 8));
                }
            }
        }

        return new ElfFile(U16(16), U16(18), U64(24), segments, symbols);
    }

    // ET_EXEC for EM_X86_64; no PT_INTERP and no PT_DYNAMIC; one executable PT_LOAD; one PT_GNU_STACK, not executable; the entry at
    // the entry symbol.
    internal void AssertStatic(string entry)
    {
        Assert.Equal((2, 62), (this.Type, this.Machine));
        Assert.DoesNotContain(this.Segments, s => s.Type is 2 or 3);
        Assert.Single(this.Segments, s => s.Type == 1 && (s.Flags & 1) != 0);
        Assert.Equal(0u, Assert.Single(this.Segments, s => s.Type == 0x6474e551).Flags & 1);
        Assert.Equal(this.Symbols[entry], this.Entry);
    }
}
