// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using Kimi.Compiler.Target;

namespace Kimi.Compiler;

// The process's library per target (PLAN G949 Stage 2 U2a; design u2-design 2.1-2.3; DECISIONS R8-M53): KimiLibrary's
// store (Of), its freeze (Freeze), its detachment (Detach) and the seam that makes the freeze compilation an unfrozen library is parsed
// in (Unfrozen; B's ruling 37: the freeze's input is its compilation, so no step reaches a compilation through a library node). Read by
// no compilation before U3.
public sealed partial class KimiLibrary
{
    // One Lazy per target (L1's one mutable static): its value is the frozen or refused library. ExecutionAndPublication keeps one freeze
    // per target, so only a compiler defect is cached as an exception (a rerun asked, incomplete query states), which the build constant
    // would reproduce; a refused library is a value, cached like a frozen one.
    private static readonly ConcurrentDictionary<TargetTriple, Lazy<KimiLibrary>> Process = new();

    // The detached data (PLAN G949 Stage 2 U3, the Front's U2b N2, form (b)): written once, by Detach, after the syntax list is taken, so a
    // faulted Detach leaves none; a read before it is the internal-invariant form, never an empty default.
    private (IReadOnlyDictionary<SourceDocument, string> Failures, IReadOnlyList<Compilation.HoverAnchor> Anchors, Kimi.Diagnostics.DetachedSyntax Syntax)? detached;

    /// <summary>Gets the frozen library's documentation failures by library document (PLAN G949 Stage 2 U2b), which a reading
    /// compilation reads from U3 (<see cref="Compilation.ReadLibrary"/>); set by the freeze's detachment, read before it the
    /// internal-invariant form.</summary>
    internal IReadOnlyDictionary<SourceDocument, string> DocumentationFailures => this.Detached.Failures;

    /// <summary>Gets the frozen library's hover anchors (PLAN G949 Stage 2 U2b), which a reading compilation reads from U3; set by the
    /// freeze's detachment, read before it the internal-invariant form.</summary>
    internal IReadOnlyList<Compilation.HoverAnchor> HoverAnchors => this.Detached.Anchors;

    /// <summary>Gets the frozen library module's syntax list as detached data (PLAN G949 Stage 2 U2b; form (R)): empty unless the freeze
    /// refused; a reading compilation registers it read-only from U3. Set by the freeze's detachment, read before it the
    /// internal-invariant form.</summary>
    internal Kimi.Diagnostics.DetachedSyntax SyntaxRecords => this.Detached.Syntax;

    private (IReadOnlyDictionary<SourceDocument, string> Failures, IReadOnlyList<Compilation.HoverAnchor> Anchors, Kimi.Diagnostics.DetachedSyntax Syntax) Detached
        => this.detached ?? throw new InvalidOperationException(
            "The Kimi library is not detached: its freeze compilation holds its documentation failures, hover anchors and syntax records (InternalInvariant; PLAN G949 Stage 2 U3).");

    /// <summary>Gets the process's library for a target (SPEC 22.1: a compiler-build constant over the language version, the embedded
    /// sources and the target), frozen once by its first reader, or refused.</summary>
    /// <param name="target">The target the reading compilation's Binding is made for; <see cref="TargetTriple.Invalid"/> is a key of its
    /// own.</param>
    /// <returns>The library, frozen or refused.</returns>
    internal static KimiLibrary Of(TargetTriple target)
        => Process.GetOrAdd(target, static key => new(() => FreezeDefault(key), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>Makes the freeze compilation of an unfrozen library: over an empty program, prepared with the target (none for
    /// <see cref="TargetTriple.Invalid"/>) and the project, with documentation and hover collection on. It is given the library made by the
    /// one constructor, parsed in that compilation (its conditions, documentation flags, identifier table and collection), before its Binding
    /// exists (<see cref="Compilation.UseLibrary"/>); a test edits or extends <see cref="Compilation.Library"/> before <see cref="Freeze"/>.</summary>
    /// <param name="target">The target.</param>
    /// <param name="kimigayo">The compiler service the compilation takes.</param>
    /// <param name="project">The preparation: the project's Debug and compile-time settings.</param>
    /// <returns>The prepared freeze compilation.</returns>
    internal static Compilation Unfrozen(TargetTriple target, Kimigayo kimigayo, Project project)
    {
        var compilation = new Compilation(kimigayo, project) { CollectDocumentation = true, CollectHover = true };
        if (target != TargetTriple.Invalid && !compilation.Prepare(target.Value))
        {
            throw new InvalidOperationException($"The library's target `{target.Value}` does not prepare (InternalInvariant; PLAN G949 Stage 2).");
        }

        compilation.UseLibrary(new KimiLibrary(compilation)); // Its Binding is given the library made in it, never Of's (s21c).
        return compilation;
    }

    /// <summary>Freezes the library of a freeze compilation: it binds Final by the one driver, whose library pass either freezes the library
    /// or refuses it (its refusal kept in <see cref="Layer"/>). Never calls <see cref="Of"/>.</summary>
    /// <param name="freeze">A compilation from <see cref="Unfrozen"/>.</param>
    /// <returns>Its library, frozen or refused.</returns>
    internal static KimiLibrary Freeze(Compilation freeze)
    {
        freeze.Bind();
        var library = freeze.Library;
        library.Detach(freeze);
        return library;
    }

    // Of's freeze: the default preparation, an empty project of a console-less compiler service (Debug off, no settings).
    private static KimiLibrary FreezeDefault(TargetTriple target)
    {
        var kimigayo = new Kimigayo(new EmptyConsoleService());
        return Freeze(Unfrozen(target, kimigayo, new Project(kimigayo)));
    }

    // The detachment (PLAN G949 Stage 2 U2b; design 5.2, P-d's list): the documentation failures, hover anchors and syntax list of the
    // library's module are taken out of the freeze compilation as data (the syntax list moved, form (R)); each documentation source forms
    // its comments' text and drops its owner; each library code context and the module drop their collection and compilation, so no
    // frozen object names the freeze compilation, which is then dropped.
    private void Detach(Compilation freeze)
    {
        var (failures, anchors) = freeze.HoverDataOf(this.Kotonoha);
        var syntax = freeze.Diagnostics.TakeSyntax(this.Kotonoha);
        this.detached = (failures, anchors, syntax);
        var documentation = this.Kotonoha.DocumentationSources;
        for (var i = 0; i < documentation.Count; i++)
        {
            documentation[i].Detach();
        }

        var contexts = new HashSet<CodeContext>(ReferenceEqualityComparer.Instance) { this.Kotonoha.RootKoto.CodeContext };
        foreach (var node in this.Kotonoha.Syntax.Nodes)
        {
            if (node?.CodeContext is { } context && ReferenceEquals(context.Kotonoha, this.Kotonoha))
            {
                contexts.Add(context);
            }
        }

        foreach (var context in contexts)
        {
            context.Detach();
        }

        this.Kotonoha.Detach();
    }
}
