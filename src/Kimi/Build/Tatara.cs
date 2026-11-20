// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Kimi.Diagnostics;

namespace Kimi.Build;

/// <summary>tatara.dll (src/Tatara, its contract in tatara.h): loaded once per path and kept mapped until the process ends, checked at
/// its load (its ABI is the SHA-256 of the tatara.h embedded in this assembly, its LLVM version the catalog's), its calls serialized
/// by one lock. A missing file, a load or bind failure and a mismatch are each an InvalidDataException naming the path, the values and
/// the remedy; a call's return of 1 or 2 is an IOException naming the operation with Tatara's message; both are the toolchain's
/// failure (IsToolchainFailure). Inside the lock a call pins its inputs, copies the result's texts and releases the result.</summary>
internal sealed unsafe class Tatara
{
    // A missing or unloadable DLL: setup for the selected toolchain. Another ABI or LLVM version: never setup on that root (it may be
    // the shared one), but a compiler of its revision or a root of the checkout's own.
    private static readonly string Remedy = $"Run {NativeToolchain.SetupCommand} for the selected toolchain.";

    private static readonly string StartRemedy =
        "This compiler's tatara.h or LLVM version differs from the toolchain's tatara.dll: use a compiler of the toolchain's revision, " +
        $"or a root of this checkout's own ({NativeToolchain.SetupCommand} -ToolchainRoot <a new directory>, then --ToolchainRoot or KIMI_TOOLCHAIN_ROOT).";

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Tatara> Loaded = new(StringComparer.OrdinalIgnoreCase);

    private readonly delegate* unmanaged[Cdecl]<Text*, Text, Text, nint*, int> optimize;
    private readonly delegate* unmanaged[Cdecl]<Text*, Text, Text, int, nint*, int> emit;
    private readonly delegate* unmanaged[Cdecl]<Text, nint*, int> inspect;
    private readonly delegate* unmanaged[Cdecl]<Text*, nuint, nint*, int> link;
    private readonly delegate* unmanaged[Cdecl]<nint, int, Text> text;
    private readonly delegate* unmanaged[Cdecl]<nint, int, int> flag;
    private readonly delegate* unmanaged[Cdecl]<nint, void> release;

    private Tatara(string path, nint library)
    {
        this.Path = path;
        this.optimize = (delegate* unmanaged[Cdecl]<Text*, Text, Text, nint*, int>)NativeLibrary.GetExport(library, "tatara_optimize");
        this.emit = (delegate* unmanaged[Cdecl]<Text*, Text, Text, int, nint*, int>)NativeLibrary.GetExport(library, "tatara_emit");
        this.inspect = (delegate* unmanaged[Cdecl]<Text, nint*, int>)NativeLibrary.GetExport(library, "tatara_inspect");
        this.link = (delegate* unmanaged[Cdecl]<Text*, nuint, nint*, int>)NativeLibrary.GetExport(library, "tatara_link");
        this.text = (delegate* unmanaged[Cdecl]<nint, int, Text>)NativeLibrary.GetExport(library, "tatara_text");
        this.flag = (delegate* unmanaged[Cdecl]<nint, int, int>)NativeLibrary.GetExport(library, "tatara_flag");
        this.release = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(library, "tatara_release");
        var identity = stackalloc nint[2];
        ((delegate* unmanaged[Cdecl]<nint*, void>)NativeLibrary.GetExport(library, "tatara_identity"))(identity);
        this.Build = Marshal.PtrToStringUTF8(identity[0]);
        this.LlvmVersion = Marshal.PtrToStringUTF8(identity[1]);
        this.Abi = Marshal.PtrToStringUTF8(((delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(library, "tatara_abi"))());
    }

    private delegate int Invocation(Text* texts, nint* result);

    // The SHA-256 (lower-case hex) of the tatara.h this compiler was built with: the ABI a DLL must report.
    internal static string ExpectedAbi { get; } = HashHeader();

    internal string Path { get; }

    internal string? Build { get; }

    internal string? LlvmVersion { get; }

    internal string? Abi { get; }

    // The first cause of a call's return of 2 (tatara.h: the instance refuses every later call), or null while none returned 2.
    internal string? Refusal { get; private set; }

    // <root>/tatara.dll, loaded and checked once per path.
    internal static Tatara Load(string root)
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "tatara.dll"));
        lock (Gate)
        {
            if (Loaded.TryGetValue(path, out var loaded))
            {
                return loaded;
            }

            if (!File.Exists(path))
            {
                throw new InvalidDataException($"Tatara not found: {path}. {Remedy}");
            }

            Tatara tatara;
            try
            {
                tatara = new Tatara(path, NativeLibrary.Load(path));
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                throw new InvalidDataException($"Tatara cannot be loaded: {path}: {ex.Message} {Remedy}", ex);
            }

            CheckStart(path, tatara.Abi, tatara.LlvmVersion);
            Loaded.Add(path, tatara);
            return tatara;
        }
    }

    // The first refusal among the loaded instances (a session ends on it, Tatara design A3 §2.4), or null.
    internal static string? FirstRefusal()
    {
        lock (Gate)
        {
            return Loaded.Values.Select(static x => x.Refusal).FirstOrDefault(static x => x is not null);
        }
    }

    // The start check: the DLL's ABI and LLVM version against this compiler's.
    internal static void CheckStart(string path, string? abi, string? llvmVersion)
    {
        if (abi != ExpectedAbi || llvmVersion != ToolchainIdentity.LlvmVersion)
        {
            throw new InvalidDataException(
                $"Tatara does not match this compiler: {path}; abi {abi ?? "(none)"}, expected {ExpectedAbi}; " +
                $"llvmVersion {llvmVersion ?? "(none)"}, expected {ToolchainIdentity.LlvmVersion}. {StartRemedy}");
        }
    }

    internal void Optimize(NativeExecutorContext context, string ir, string optimizedIr)
        => this.Call("optimize", context, [.. Target(context), ir, optimizedIr], (texts, result) => this.optimize(texts, texts[5], texts[6], result));

    internal void Emit(NativeExecutorContext context, string ir, string obj, int optimizationLevel)
        => this.Call("emit", context, [.. Target(context), ir, obj], (texts, result) => this.emit(texts, texts[5], texts[6], optimizationLevel, result));

    internal ObjectFacts Inspect(NativeExecutorContext context, string obj)
        => this.Call("inspect", context, [obj], (texts, result) => this.inspect(texts[0], result));

    internal void Link(NativeExecutorContext context, IReadOnlyList<string> arguments)
        => this.Call("link", context, [.. arguments], (texts, result) => this.link(texts, (nuint)arguments.Count, result));

    private static string HashHeader()
    {
        using var stream = typeof(Tatara).Assembly.GetManifestResourceStream("Kimi.Tatara.h")!;
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    // TataraTarget's five values, in its order.
    private static string[] Target(NativeExecutorContext context)
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

    // One call: its inputs encoded into one buffer and pinned, the result's texts copied and the result released, all inside the lock.
    // A return of 1 or 2 throws with Tatara's message; a message on success is a Warning, as a tool's standard error is.
    private ObjectFacts Call(string operation, NativeExecutorContext context, string[] values, Invocation invocation)
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

                message = this.text(result, 0).Read();
                undefined = this.text(result, 1).Read();
                defined = this.text(result, 2).Read();
                hasUnwind = this.flag(result, 0) == 1;
                hasDefaultLibrary = this.flag(result, 1) == 1;
                if (code == 2)
                {
                    this.Refusal ??= message;
                }
            }
            finally
            {
                if (result != 0)
                {
                    this.release(result);
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
