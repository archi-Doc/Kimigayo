// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Kimi.Diagnostics;

namespace Kimi.Build;

/// <summary>The compiler's Tatara (src/Tatara, its contract in tatara.h): linked into kimi.exe, or tatara.dll beside Kimi.dll, which the
/// build of Kimi copies from the checkout's toolchain (Tatara B6b1). Its nine exports are this type's LibraryImport declarations, which
/// in the managed form search only the assembly's directory and System32, never PATH. One instance per process (<see cref="Instance"/>),
/// created at its first use and checked then (its ABI is the SHA-256 of the tatara.h embedded in this assembly, its LLVM version the
/// catalog's); a failed load or check keeps no instance, so the next use tries again. The check calls two exports; the other seven bind
/// at their first call, and a library that passes the ABI check but lacks one broke the ABI contract (build.ps1's export-list check
/// rules it out), so the EntryPointNotFoundException that call raises is a fault, not the toolchain's failure. Its calls are serialized
/// by one lock. A load failure and a mismatch are each an InvalidDataException naming the values and the remedy; a call's return of 1
/// or 2 is an IOException naming the operation with Tatara's message; both are the toolchain's failure (IsToolchainFailure).
/// NativeBuild is its caller (and toolchain verify reports it). A call checks its token before the lock and again inside it, so a call
/// that waited for the lock does not start after cancellation; inside the lock it pins its inputs, copies the result's texts and flags
/// (an inspection's, <see cref="Inspection"/>) and releases the result; parsing an inspection (<see cref="Facts"/>) runs outside it.</summary>
internal sealed unsafe partial class Tatara
{
    // Neither remedy leads to setup into a shared or linked toolchain (a worktree's toolchain is a junction to the shared root, which
    // only a window's setup writes): a toolchain directory of the checkout's own, or the toolchain's revision.
    private static readonly string LoadRemedy =
        $"Run src/backend/windows-x64/setup.ps1 -LlvmRoot <the LLVM {ToolchainIdentity.LlvmVersion} archive, extracted> for a toolchain " +
        "directory of this checkout's own (not a junction to the shared root), or use a compiler of the toolchain's revision; then build " +
        "Kimi again.";

    private static readonly string StartRemedy =
        "This compiler's tatara.h or LLVM version differs from the tatara.dll it was built with: build Kimi again with the toolchain of " +
        "its revision (a toolchain directory of this checkout's own, or a merge to the toolchain's revision).";

    private static readonly Lock Gate = new();

    private static Tatara? instance;

    // The frozen bootstrap (tatara.h): the first call loads the library in the managed form.
    private Tatara()
    {
        var identity = stackalloc nint[2];
        NativeIdentity(identity);
        this.Build = Marshal.PtrToStringUTF8(identity[0]);
        this.LlvmVersion = Marshal.PtrToStringUTF8(identity[1]);
        this.Abi = Marshal.PtrToStringUTF8(NativeAbi());
    }

    private delegate int Invocation(Text* texts, nint* result);

    // The SHA-256 (lower-case hex) of the tatara.h this compiler was built with: the ABI a DLL must report.
    internal static string ExpectedAbi { get; } = HashHeader();

    // The process's one Tatara, loaded and checked at its first use.
    internal static Tatara Instance
    {
        get
        {
            lock (Gate)
            {
                if (instance is null)
                {
                    Tatara tatara;
                    try
                    {
                        tatara = new Tatara();
                    }
                    catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
                    {
                        throw new InvalidDataException(
                            $"Tatara cannot be loaded: tatara.dll beside the compiler ({AppContext.BaseDirectory}): {ex.Message} {LoadRemedy}",
                            ex);
                    }

                    CheckStart(tatara.Abi, tatara.LlvmVersion);
                    instance = tatara;
                }

                return instance;
            }
        }
    }

    internal string? Build { get; }

    internal string? LlvmVersion { get; }

    internal string? Abi { get; }

    // The first cause of a call's return of 2 (tatara.h: the instance refuses every later call), or null while none returned 2.
    internal string? Refusal { get; private set; }

    // The instance's refusal (a session ends on it, Tatara design A3 §2.4), or null, also while none is loaded.
    internal static string? FirstRefusal()
    {
        lock (Gate)
        {
            return instance?.Refusal;
        }
    }

    // The start check: the library's ABI and LLVM version against this compiler's.
    internal static void CheckStart(string? abi, string? llvmVersion)
    {
        if (abi != ExpectedAbi || llvmVersion != ToolchainIdentity.LlvmVersion)
        {
            throw new InvalidDataException(
                $"Tatara does not match this compiler: abi {abi ?? "(none)"}, expected {ExpectedAbi}; " +
                $"llvmVersion {llvmVersion ?? "(none)"}, expected {ToolchainIdentity.LlvmVersion}. {StartRemedy}");
        }
    }

    // An inspection's facts, the one parser of inspect's texts (tatara.h): per member in archive order its form (text kind 4) and its
    // blocks of kinds 1, 2, 5 and 6; the archive's index (kind 7); flags 1 and 2. It loads no Tatara.
    internal static ObjectFacts Facts(Inspection inspection)
    {
        var forms = inspection.Forms;
        var (undefined, defined) = (Blocks(inspection.Undefined), Blocks(inspection.Defined));
        var (comdats, directives) = (Blocks(inspection.Comdats), Blocks(inspection.Directives));
        var members = new ObjectMember[forms.Length / 2];
        for (var i = 0; i < members.Length; i++)
        {
            members[i] = new ObjectMember(forms[2 * i], [.. undefined[i].Select(Symbol)], [.. defined[i].Select(Symbol)], comdats[i], [.. directives[i].Select(Directive)]);
        }

        var index = inspection.Index.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return new ObjectFacts(members, [.. index.Select(Entry)], inspection.Archive, inspection.DefaultLibrary);
    }

    // Verifies ir and optimizes it at default<O2>: the text opt -S writes, which the caller writes (tatara.h).
    internal string Optimize(TataraContext context, string ir)
        => Encoding.UTF8.GetString(this.Call("optimize", context, [.. Target(context), ir], (texts, result) => NativeOptimize(texts, texts[5], result)).Output);

    // Verifies ir, then compiles it as llc -O0 or -O2 does (the level's digit): the object's bytes, which the caller writes; obj only
    // names it (llc's -o, the name its debug information gives it) and is not opened (tatara.h).
    internal byte[] Emit(TataraContext context, string ir, string obj, int optimizationLevel)
        => this.Call("emit", context, [.. Target(context), ir, obj], (texts, result) => NativeEmit(texts, texts[5], texts[6], optimizationLevel, result)).Output;

    // An object's or an archive's facts (tatara.h's inspect: one member per archive member, an object one): its inspection, parsed.
    internal ObjectFacts Inspect(TataraContext context, string obj) => Facts(this.InspectTexts(context, obj));

    // An object's or an archive's inspection: inspect's texts and flags as Tatara returned them (tatara.h).
    internal Inspection InspectTexts(TataraContext context, string obj)
        => this.Call("inspect", context, [obj], (texts, result) => NativeInspect(texts[0], result)).Inspection;

    // Links with LLD's arguments, as NativeBuild builds them: arguments[0] names the flavor, lld-link or ld.lld (tatara.h).
    internal void Link(TataraContext context, IReadOnlyList<string> arguments)
        => this.Call("link", context, [.. arguments], (texts, result) => NativeLink(texts, (nuint)arguments.Count, result));

    private static string HashHeader()
    {
        using var stream = typeof(Tatara).Assembly.GetManifestResourceStream("Kimi.Tatara.h")!;
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    // TataraTarget's five values, in its order.
    private static string[] Target(TataraContext context)
        => [context.Triple, context.Cpu, context.Features, context.RelocationModel, context.CodeModel];

    // Inspect's texts and flags in a result (tatara.h), copied. Another operation sets none of them: empty texts, no member.
    private static Inspection Copy(nint result) => new(
        NativeText(result, 1).Read(), NativeText(result, 2).Read(), NativeText(result, 4).Read(), NativeText(result, 5).Read(), NativeText(result, 6).Read(), NativeText(result, 7).Read(), NativeFlag(result, 1) == 1, NativeFlag(result, 2) == 1);

    // A per-member text (kinds 1, 2, 5 and 6): each member's lines, ended by an empty line.
    private static List<string[]> Blocks(string text)
    {
        var lines = text.Split('\n');
        var blocks = new List<string[]>();
        for (int i = 0, start = 0; i < lines.Length - 1; i++)
        {
            if (lines[i].Length == 0)
            {
                blocks.Add(lines[start..i]);
                start = i + 1;
            }
        }

        return blocks;
    }

    // "<llvm-nm's letter> <name>" (kinds 1 and 2).
    private static ObjectSymbol Symbol(string line) => new(line[2..], line[0]);

    // "I <name>" or "A <from>=<to>" (kind 6; <from> holds no '=').
    private static ObjectDirective Directive(string line)
    {
        var equals = line.IndexOf('=', 2);
        return line[0] == 'A' ? new(line[0], line[2..equals], line[(equals + 1)..]) : new(line[0], line[2..], null);
    }

    // "<member> <name>" (kind 7).
    private static ArchiveSymbol Entry(string line)
    {
        var space = line.IndexOf(' ');
        return new(int.Parse(line.AsSpan(0, space), CultureInfo.InvariantCulture), line[(space + 1)..]);
    }

    // tatara.h's nine functions. The search (the managed form's; kimi.exe's link binds them without one): the assembly's directory
    // (Kimi.dll's copy) and System32, so a tatara.dll on PATH is never loaded in its place.
    [LibraryImport("tatara", EntryPoint = "tatara_identity")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial void NativeIdentity(nint* identity);

    [LibraryImport("tatara", EntryPoint = "tatara_abi")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial nint NativeAbi();

    [LibraryImport("tatara", EntryPoint = "tatara_optimize")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial int NativeOptimize(Text* target, Text ir, nint* result);

    [LibraryImport("tatara", EntryPoint = "tatara_emit")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial int NativeEmit(Text* target, Text ir, Text obj, int optimizationLevel, nint* result);

    [LibraryImport("tatara", EntryPoint = "tatara_inspect")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial int NativeInspect(Text obj, nint* result);

    [LibraryImport("tatara", EntryPoint = "tatara_link")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial int NativeLink(Text* arguments, nuint count, nint* result);

    [LibraryImport("tatara", EntryPoint = "tatara_text")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial Text NativeText(nint result, int kind);

    [LibraryImport("tatara", EntryPoint = "tatara_flag")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial int NativeFlag(nint result, int kind);

    [LibraryImport("tatara", EntryPoint = "tatara_release")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]
    private static partial void NativeRelease(nint result);

    // One call: its inputs encoded into one buffer and pinned, the result's texts and flags copied and the result released, all inside
    // the lock. A return of 1 or 2 throws with Tatara's message; a message on success is a Warning, as a tool's standard error is.
    // Returns the inspection (empty texts for another operation), which the caller parses outside the lock, and the output (text kind 3;
    // empty for inspect and link).
    private (Inspection Inspection, byte[] Output) Call(string operation, TataraContext context, string[] values, Invocation invocation)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var lengths = new int[values.Length];
        var buffer = new byte[values.Sum(Encoding.UTF8.GetByteCount)];
        for (int i = 0, start = 0; i < values.Length; start += lengths[i++])
        {
            lengths[i] = Encoding.UTF8.GetBytes(values[i], buffer.AsSpan(start));
        }

        int code;
        string message;
        Inspection inspection;
        byte[] output;
        lock (Gate)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            nint result = 0;
            try
            {
                fixed (byte* bytes = buffer)
                {
                    var texts = stackalloc Text[values.Length];
                    for (int i = 0, start = 0; i < values.Length; start += lengths[i++])
                    {
                        texts[i] = new Text((nint)(bytes + start), lengths[i]);
                    }

                    code = invocation(texts, &result);
                }

                message = NativeText(result, 0).Read();
                inspection = code == 0 ? Copy(result) : default;
                output = NativeText(result, 3).Bytes();
                if (code == 2)
                {
                    this.Refusal ??= message;
                }
            }
            finally
            {
                if (result != 0)
                {
                    NativeRelease(result);
                }
            }
        }

        if (code != 0)
        {
            throw new IOException($"Tatara failed: {operation}\n{message}");
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            context.Report(DiagnosticSeverity.Warning, message.Trim());
        }

        return (inspection, output);
    }

    /// <summary>An inspection as tatara.h's inspect returns it, before <see cref="Facts"/> parses it: the texts of kinds 1, 2, 4, 5, 6
    /// and 7, in kind order, and flags 1 and 2.</summary>
    /// <param name="Undefined">Text kind 1: each member's undefined symbols, a block per member.</param>
    /// <param name="Defined">Text kind 2: each member's defined external symbols, a block per member.</param>
    /// <param name="Forms">Text kind 4: each member's form, a line per member.</param>
    /// <param name="Comdats">Text kind 5: each member's definitions that admit copies, a block per member.</param>
    /// <param name="Directives">Text kind 6: each member's .drectve references, a block per member.</param>
    /// <param name="Index">Text kind 7: the archive's symbol index, a line per entry.</param>
    /// <param name="DefaultLibrary">Flag kind 1: a member names a default library.</param>
    /// <param name="Archive">Flag kind 2: the input is an archive.</param>
    internal readonly record struct Inspection(string Undefined, string Defined, string Forms, string Comdats, string Directives, string Index, bool DefaultLibrary, bool Archive);

    // TataraText: bytes and their length, not NUL-terminated; UTF-8 text but emit's output (an object's bytes).
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Text
    {
        private readonly nint bytes;
        private readonly nint length;

        internal Text(nint bytes, nint length)
        {
            this.bytes = bytes;
            this.length = length;
        }

        internal string Read() => this.length == 0 ? string.Empty : Encoding.UTF8.GetString((byte*)this.bytes, (int)this.length);

        internal byte[] Bytes() => new ReadOnlySpan<byte>((byte*)this.bytes, (int)this.length).ToArray();
    }
}

/// <summary>What Tatara's calls take, and nothing else: the host's report (a call's message on success, as a Warning) and cancellation
/// (checked before and inside the lock), and the target values of TataraTarget, in its order (the build's row's).</summary>
internal sealed record TataraContext(
    Action<DiagnosticSeverity, string> Report,
    CancellationToken CancellationToken,
    string Triple,
    string Cpu,
    string Features,
    string RelocationModel,
    string CodeModel);

/// <summary>A symbol of an object: its name and llvm-nm's letter for its kind (U, T, D, B, R, ...).</summary>
internal readonly record struct ObjectSymbol(string Name, char Kind);

/// <summary>What <see cref="Tatara.Inspect"/> reads from an object or an archive (tatara.h's inspect texts and flags): <c>Members</c>, one
/// per archive member in archive order, an object's one; <c>Index</c>, the archive's symbol index in its order (text kind 7; empty for
/// an object or an archive without one); <c>IsArchive</c> (flag kind 2); <c>HasDefaultLibrary</c>, a member names a default library
/// (flag kind 1). NativeBuild judges its module object's and writes the record's form.</summary>
internal readonly record struct ObjectFacts(IReadOnlyList<ObjectMember> Members, IReadOnlyList<ArchiveSymbol> Index, bool IsArchive, bool HasDefaultLibrary);

/// <summary>A member of an inspected input (an object is one): <c>Form</c> (text kind 4: <c>o</c> a relocatable object, <c>i</c> a COFF
/// short import member, <c>s</c> a linked image); <c>Undefined</c> and <c>DefinedExternal</c>, its symbols in llvm-nm's order (kinds 1
/// and 2); <c>Comdats</c>, the defined names whose section admits copies (kind 5); <c>Directives</c>, its .drectve's references in
/// their order (kind 6).</summary>
internal readonly record struct ObjectMember(char Form, IReadOnlyList<ObjectSymbol> Undefined, IReadOnlyList<ObjectSymbol> DefinedExternal, IReadOnlyList<string> Comdats, IReadOnlyList<ObjectDirective> Directives);

/// <summary>A reference a COFF member's .drectve makes (text kind 6): <c>Kind</c> <c>I</c>, /include:'s <c>Name</c> (no
/// <c>Target</c>); <c>A</c>, /alternatename:'s <c>Name</c>=<c>Target</c>.</summary>
internal readonly record struct ObjectDirective(char Kind, string Name, string? Target);

/// <summary>An entry of an archive's symbol index (text kind 7): the <c>Member</c> it names, by position in
/// <see cref="ObjectFacts.Members"/>, and the symbol's <c>Name</c>.</summary>
internal readonly record struct ArchiveSymbol(int Member, string Name);
