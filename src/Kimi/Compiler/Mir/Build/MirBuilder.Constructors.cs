// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Constructors (SPEC 6.3.3): the steps that build a constructor's object in its return local from fresh storage before its body runs.
internal sealed partial class MirBuilder
{
    // The base constructor into the base subobject, then this layer's Field initializers in declaration order, each into its Field.
    private void Construct(BoundType self, FunctionKoto constructor)
    {
        if (constructor.BaseInitializer is { } baseCall)
        {
            this.Initialize(this.body.Project(new(0), MirProjectionKind.Base, 1), baseCall);
        }

        for (var i = 0; i < AdtDef.Count(self); i++)
        {
            if (AdtDef.Field(self, i) is { InitializerKoto: { } initializer } field)
            {
                this.Initialize(this.body.Project(new(0), MirProjectionKind.Field, i), initializer, HirTables.Op(field) != HirOp.Body);
            }
        }
    }

    // A step of a constructor as a statement of its own (SPEC 6.3.3): the base call into the base subobject, or a Field's initializer,
    // as its Adjust takes it, into the Field; that of a Field declaration Binding did not bind (no Body) is derived from its error.
    private void Initialize(MirPlace place, Koto node, bool rejected = false)
    {
        this.statementStart = true;
        var temporaries = this.OpenScope(block: false);
        if (rejected)
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
