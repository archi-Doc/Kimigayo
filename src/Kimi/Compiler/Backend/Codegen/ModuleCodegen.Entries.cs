// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Compiler.Backend;

// The interrupt entries' signatures of ModuleCodegen (IMPL 21.6; DECISIONS T-M<OSF3b>), whose thunks FunctionCodegen.Entries writes: an
// InterruptEntry item's x86_intrcc function, named __kimi_f<item> (FunctionPrefix, which an entry call's code address spells), its
// parameters its variant's (InterruptEntries: the frame, then the error code where the processor pushes one), kept across Writes through
// the per-index cache of ModuleCodegen.Signatures.cs.
internal sealed partial class ModuleCodegen
{
    private static readonly AbiParameter[] FrameParameters = [new("ptr", "frame", LogicalIndex: 0)];

    private static readonly AbiParameter[] CodeParameters = [new("ptr", "frame", LogicalIndex: 0), new("i64", "code", LogicalIndex: 1)];

    /// <summary>Gets the signature of an InterruptEntry item's thunk (IMPL 21.6): <c>void</c>, the frame's address (<c>ptr</c>, which its
    /// header passes <c>byval</c>) and, for a variant whose processor pushes an error code, the code (<c>i64</c>), named
    /// <see cref="FunctionPrefix"/> with its index; named once per Write, the previous one kept while it has the same parameters.</summary>
    /// <param name="item">The InterruptEntry item.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Thunk(int item)
    {
        this.Requested(item);
        if (this.Stamped(ref this.signatures, ref this.signed, item) is { } current)
        {
            return current;
        }

        var parameters = InterruptEntries.Row(this.set.Items[item].Key.Variant).Code ? CodeParameters : FrameParameters;
        this.signed[item] = this.writes;
        return this.signatures[item] is { } previous && ReferenceEquals(previous.Parameters, parameters)
            ? previous : this.signatures[item] = new(FunctionPrefix + item.ToString(CultureInfo.InvariantCulture), "void", parameters);
    }
}
