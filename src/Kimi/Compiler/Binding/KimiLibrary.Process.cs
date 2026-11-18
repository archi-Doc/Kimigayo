// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using Kimi.Compiler.Target;

namespace Kimi.Compiler;

// The process's library per target (PLAN G949 Stage 2 U2a; design u2-design 2.1-2.3; DECISIONS R8-M?? on landing): KimiLibrary's
// store (Of), its freeze (Freeze) and the seam that makes the freeze compilation an unfrozen library is parsed in (Unfrozen; B's ruling
// 37: the freeze's input is its compilation, so no step reaches a compilation through a library node). Read by no
// compilation before U3.
public sealed partial class KimiLibrary
{
    // One Lazy per target (L1's one mutable static): its value is the frozen or refused library. ExecutionAndPublication keeps one freeze
    // per target, so only a compiler defect is cached as an exception (a rerun asked, incomplete query states), which the build constant
    // would reproduce; a refused library is a value, cached like a frozen one.
    private static readonly ConcurrentDictionary<TargetTriple, Lazy<KimiLibrary>> Process = new();

    /// <summary>Gets the process's library for a target (SPEC 22.1: a compiler-build constant over the language version, the embedded
    /// sources and the target), frozen once by its first reader, or refused.</summary>
    /// <param name="target">The target the reading compilation's Binding is made for; <see cref="TargetTriple.Invalid"/> is a key of its
    /// own.</param>
    /// <returns>The library, frozen or refused.</returns>
    internal static KimiLibrary Of(TargetTriple target)
        => Process.GetOrAdd(target, static key => new(() => FreezeDefault(key), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>Makes the freeze compilation of an unfrozen library: over an empty program, prepared with the target (none for
    /// <see cref="TargetTriple.Invalid"/>) and the project, with documentation and hover collection on. Its Binding makes the library by the
    /// one constructor, parsed in that compilation (its conditions, documentation flags, identifier table and collection); a test edits or
    /// extends <see cref="Compilation.Library"/> before <see cref="Freeze"/>.</summary>
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

        return compilation;
    }

    /// <summary>Freezes the library of a freeze compilation: it binds Final by the one driver, whose library pass either freezes the library
    /// or refuses it (its refusal kept in <see cref="Layer"/>). Never calls <see cref="Of"/>.</summary>
    /// <param name="freeze">A compilation from <see cref="Unfrozen"/>.</param>
    /// <returns>Its library, frozen or refused.</returns>
    internal static KimiLibrary Freeze(Compilation freeze)
    {
        freeze.Bind();
        return freeze.Library;
    }

    // Of's freeze: the default preparation, an empty project of a console-less compiler service (Debug off, no settings).
    private static KimiLibrary FreezeDefault(TargetTriple target)
    {
        var kimigayo = new Kimigayo(new EmptyConsoleService());
        return Freeze(Unfrozen(target, kimigayo, new Project(kimigayo)));
    }
}
