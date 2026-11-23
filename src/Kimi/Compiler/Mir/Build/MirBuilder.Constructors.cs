// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Constructors (SPEC 6.3.3): the steps that build a constructor's object in its return local from fresh storage before its body runs.
internal sealed partial class MirBuilder
{
    // The base constructor into the base subobject, then this layer's Field initializers in declaration order, each into its Field (Stored:
    // of no Type for a Field whose own slot failed, PLAN G1184); a part the instance does not store (Stored: past IMPL A.4's height or not
    // substituted) has no Place, nor a base whose clause Binding formed no Type for (AdtDef.Base, as every Base step reads it), so its
    // initializer or base call, a part Binding rejected, is not placed.
    private void Construct(BoundType self, FunctionKoto constructor)
    {
        if (constructor.BaseInitializer is { } baseCall && AdtDef.Base(compilation.Types, self) is not null)
        {
            this.Initialize(this.body.Project(compilation.Types, new(0), MirProjectionKind.Base, 1), baseCall);
        }

        for (var i = 0; i < AdtDef.Count(self); i++)
        {
            if (AdtDef.Field(self, i) is { InitializerKoto: { } initializer } && this.Stored(new(0), self, 0, i) is { } place)
            {
                this.Initialize(place, initializer);
            }
        }
    }

    // A step of a constructor as a statement of its own (SPEC 6.3.3): the base call into the base subobject, or a Field's initializer,
    // as its Adjust takes it, into the Field; that of a Field declaration Binding did not bind is derived from its error (DECISIONS Z16:
    // the failure fact at the step's declaration, not published Body; the constructor being built is).
    private void Initialize(MirPlace place, Koto node)
    {
        this.statementStart = true;
        var temporaries = this.OpenScope(block: false);
        if (HirTables.Op(node.Parent!) != HirOp.Body)
        {
            this.Opaque(node, place);
        }
        else if (HirTables.AdjustOf(node).Acquire == HirAcquire.Unset)
        {
            this.Into(place, node);
        }
        else
        {
            this.Assign(place, node);
        }

        this.CloseScope(temporaries);
    }
}
