// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Backend;

namespace Kimi.Compiler;

/// <summary>What a written module states for its link, the generation result beside the module text (R9-B1; Compilation.Generated):
/// published by the generation entry from the decisions it and code generation made, and read by the toolchain's publication, which
/// writes it into the link manifest and reads no IR text and no Binding state.</summary>
/// <param name="OutputKind">The module's output form: a Library has no entry; an Application's and a test build's module has one.</param>
/// <param name="Libraries">The native libraries the module requires, the library and Kind of every validated <c>#LibraryImport</c>, one
/// per library (ModuleCodegen.Libraries).</param>
/// <param name="Externals">The external functions the module declares, one per symbol in declaration order, each read for its
/// <c>Symbol</c> and <c>DllImport</c> (ModuleCodegen.Externals).</param>
internal readonly record struct GeneratedModule(
    OutputKind OutputKind,
    IReadOnlyList<(string Library, string Kind)> Libraries,
    IReadOnlyList<ExternalFunction> Externals);
