// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Build;

/// <summary>The work of a native build that runs LLVM or LLD: four calls, in the order NativeBuild makes them. NativeBuild owns the
/// steps, the verdicts, the redaction between <see cref="Optimize"/> and <see cref="Emit"/> and the record's form; an executor only
/// runs the work and reads the object's facts. Every path an executor receives is absolute. A failure is the toolchain's
/// (IsToolchainFailure). Temporary seam (Tatara A2 to A4):
/// unit A4 deletes the external implementation and this type.</summary>
internal abstract class NativeExecutor
{
    // Verifies ir, optimizes it at default<O2> and writes the text to optimizedIr as opt -S does.
    internal abstract Task Optimize(string ir, string optimizedIr);

    // Verifies ir, then writes the object as llc -O0 or -O2 does.
    internal abstract Task Emit(string ir, string obj, string optimization);

    internal abstract Task<ObjectFacts> Inspect(string obj);

    // Links with the linker's arguments, as NativeBuild builds them.
    internal abstract Task Link(IReadOnlyList<string> arguments);
}

/// <summary>What a factory receives where NativeBuild resolves its executor: the resolved toolchain root and bin, the tools' working
/// directory (the output directory), the root the record redacts, the host's report and cancellation, and the target values
/// (WindowsProfile's). The factory resolves what it needs and fills the record's <c>tools</c> object it is given.</summary>
internal sealed record NativeExecutorContext(
    string Root,
    string Bin,
    string OutputDirectory,
    string RedactionRoot,
    Action<DiagnosticSeverity, string> Report,
    CancellationToken CancellationToken,
    string Triple,
    string Cpu,
    string Features,
    string RelocationModel,
    string CodeModel);

/// <summary>A symbol of an object: its name and llvm-nm's letter for its kind (U, T, D, B, R, ...).</summary>
internal readonly record struct ObjectSymbol(string Name, char Kind);

/// <summary>What <see cref="NativeExecutor.Inspect"/> reads from an object; NativeBuild judges them and writes the record's form.
/// <c>HasUnwind</c>: the object has unwind entries; <c>HasDefaultLibrary</c>: it names a default library.</summary>
internal readonly record struct ObjectFacts(IReadOnlyList<ObjectSymbol> Undefined, IReadOnlyList<ObjectSymbol> DefinedExternal, bool HasUnwind, bool HasDefaultLibrary);
