// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Kimi.Diagnostics;

namespace Kimi.Build;

/// <summary>The compiler's Tatara (src/Tatara, its contract in tatara.h): linked into kimi.exe, or tatara.dll beside Kimi.dll, which the
/// build of Kimi copies from the checkout's toolchain (Tatara B6b1). Its nine exports are this type's LibraryImport declarations, which
/// in the managed form search only the assembly's directory and System32, never PATH. One instance per process (<see cref="Instance"/>),
/// created at its first use and checked then (its ABI is the SHA-256 of the tatara.h embedded in this assembly, its LLVM version the
/// catalog's); a failed load or check keeps no instance, so the next use tries again. Its calls are serialized by one lock. A load
/// failure and a mismatch are each an InvalidDataException naming the values and the remedy; a call's return of 1 or 2 is an
/// IOException naming the operation with Tatara's message; both are the toolchain's failure (IsToolchainFailure). NativeBuild is its
/// caller (and toolchain verify reports it). A call checks its token before the lock and again inside it, so a call that waited for
/// the lock does not start after cancellation; inside the lock it pins its inputs, copies the result's texts and releases the
/// result.</summary>
internal sealed unsafe partial class Tatara
{
    // A mismatch: never setup on the checkout's toolchain (it may be the shared one), but Kimi rebuilt from the toolchain of its revision.
    private static readonly string StartRemedy =
        "This compiler's tatara.h or LLVM version differs from the tatara.dll it was built with: build Kimi again after setup of this " +
        "checkout's toolchain (VERIFICATION: a unit that changes src/Tatara uses a toolchain of its own).";

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
                            $"Tatara cannot be loaded: tatara.dll beside the compiler ({AppContext.BaseDirectory}): {ex.Message} " +
                            $"Run {NativeToolchain.SetupCommand}, then build Kimi again.",
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

    // Verifies ir, optimizes it at default<O2> and writes the text to optimizedIr as opt -S does.
    internal void Optimize(TataraContext context, string ir, string optimizedIr)
        => this.Call("optimize", context, [.. Target(context), ir, optimizedIr], (texts, result) => NativeOptimize(texts, texts[5], texts[6], result));

    // Verifies ir, then writes the object as llc -O0 or -O2 does (the level's digit).
    internal void Emit(TataraContext context, string ir, string obj, int optimizationLevel)
        => this.Call("emit", context, [.. Target(context), ir, obj], (texts, result) => NativeEmit(texts, texts[5], texts[6], optimizationLevel, result));

    internal ObjectFacts Inspect(TataraContext context, string obj)
        => this.Call("inspect", context, [obj], (texts, result) => NativeInspect(texts[0], result));

    // Links with the linker's arguments, as NativeBuild builds them.
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

    // Inspect's symbol text: one "<llvm-nm's letter> <name>\n" per symbol.
    private static List<ObjectSymbol> Symbols(string listing)
    {
        var symbols = new List<ObjectSymbol>();
        foreach (var line in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            symbols.Add(new ObjectSymbol(line[2..], line[0]));
        }

        return symbols;
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
    private static partial int NativeOptimize(Text* target, Text ir, Text optimizedIr, nint* result);

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

    // One call: its inputs encoded into one buffer and pinned, the result's texts copied and the result released, all inside the lock.
    // A return of 1 or 2 throws with Tatara's message; a message on success is a Warning, as a tool's standard error is.
    private ObjectFacts Call(string operation, TataraContext context, string[] values, Invocation invocation)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var lengths = new int[values.Length];
        var buffer = new byte[values.Sum(Encoding.UTF8.GetByteCount)];
        for (int i = 0, start = 0; i < values.Length; start += lengths[i++])
        {
            lengths[i] = Encoding.UTF8.GetBytes(values[i], buffer.AsSpan(start));
        }

        int code;
        string message, undefined, defined;
        bool hasUnwind, hasDefaultLibrary;
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
                undefined = NativeText(result, 1).Read();
                defined = NativeText(result, 2).Read();
                hasUnwind = NativeFlag(result, 0) == 1;
                hasDefaultLibrary = NativeFlag(result, 1) == 1;
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

        return new ObjectFacts(Symbols(undefined), Symbols(defined), hasUnwind, hasDefaultLibrary);
    }

    // TataraText: UTF-8 bytes and their length, not NUL-terminated.
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
    }
}

/// <summary>What Tatara's calls take, and nothing else: the host's report (a call's message on success, as a Warning) and cancellation
/// (checked before and inside the lock), and the target values of TataraTarget, in its order (WindowsProfile's).</summary>
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

/// <summary>What <see cref="Tatara.Inspect"/> reads from an object; NativeBuild judges them and writes the record's form.
/// <c>HasUnwind</c>: the object has unwind entries; <c>HasDefaultLibrary</c>: it names a default library.</summary>
internal readonly record struct ObjectFacts(IReadOnlyList<ObjectSymbol> Undefined, IReadOnlyList<ObjectSymbol> DefinedExternal, bool HasUnwind, bool HasDefaultLibrary);
