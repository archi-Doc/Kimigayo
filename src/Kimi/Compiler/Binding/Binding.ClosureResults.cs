// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// A Closure's inferred result over its call's own storage (SPEC 15.8.2, 15.6.1, 23.1).
// - Responsibility: CallLocalOrigin, the first Origin of an inferred result Type that names the call's own storage (a parameter slot
//   or a body local of the Closure, or, in a Consuming call, an environment binding); CallResultOrigin, the Closure's `call result`
//   atom, the shorter end of the Refuted relation BindConcreteClosure records at such a result.
// - Inputs: the result Type, the Closure, and whether its classified receiver is Consuming (Binding.ClosureReceivers.cs).
// - Outputs: the found Origin or none; the atom OriginAtom(Closure, Projection, CallResultSlot), which the record's display and
//   related header (Binding.Diagnostics.cs) and the view's CallResult role (Binding.OriginRows.cs) recognize.
// - Invariants: an Origin of the enclosing body and a receiver-dependent binding of a borrowed environment are no call-local storage;
//   a meet names call-local storage when one of its operands does; a Function Type's components are not searched.
// - Prohibitions: no record, failure or judgment of a relation (BindConcreteClosure records it).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 15.8.2: the first Origin of an inferred closure result that names the call's own storage: a parameter slot or a body local
    // of the closure, or, in a Consuming call, an environment binding. Origins of the enclosing body and receiver-dependent bindings
    // of a borrowed environment are no such storage, nor is the call receiver's Origin ρ (RoleOf; DECISIONS R4-M126 P12).
    private static BoundOrigin? CallLocalOrigin(BoundType type, FunctionKoto function, bool consuming)
    {
        if (CallLocal(type.Origin) is { } found)
        {
            return found;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (CallLocal(type.OriginArguments[i]) is { } argument)
            {
                return argument;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (type.Kind != BoundTypeKind.Function && CallLocalOrigin(type.Components[i], function, consuming) is { } part)
            {
                return part;
            }
        }

        return null;

        BoundOrigin? CallLocal(BoundOrigin? origin)
        {
            if (origin is null)
            {
                return null;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (CallLocal(origin.Operands[i]) is { } operand)
                    {
                        return operand;
                    }
                }

                return null;
            }

            return origin.Kind is OriginKind.Projection or OriginKind.Anchor && origin.Binder is { } binder &&
                (ReferenceEquals(binder, function) ? origin.Slot >= 0 || (consuming && RoleOf(origin) == ProjectionRole.Environment)
                    : IsWithin(binder, function)) ? origin : null;
        }
    }

    // SPEC 23.1: the `closure` display `call result`, the shorter end of a result that outlives its call.
    private BoundOrigin CallResultOrigin(FunctionKoto function) => this.OriginAtom(function, OriginKind.Projection, CallResultSlot);
}
