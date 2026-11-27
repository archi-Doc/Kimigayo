// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Constructors (SPEC 6.3.3): the steps that build a constructor's object in its return local from fresh storage before its body runs.
internal sealed partial class MirBuilder
{
    // Whether a layer has a published initializer: an own Field with an initializer whose declaration the HIR publishes as a value
    // (PublishedStep, Initialize's test), over the Fields the step that places them reads (Initializers). A layer whose every initializer the
    // HIR did not publish (a container beyond the case bound, a no-case definition) has no evaluated value, as its unpublished constructor
    // has none: Layered's.
    private static bool HasInitializer(AdtDef layer)
    {
        foreach (var field in layer.Fields)
        {
            if (field.InitializerKoto is { } initializer && PublishedStep(initializer))
            {
                return true;
            }
        }

        return false;
    }

    // Whether the HIR publishes a step's declaration as a value (its Op is Body): the one test by which Initialize places the step's value
    // rather than an Opaque and by which a layer has a published initializer (HasInitializer).
    private static bool PublishedStep(Koto step) => HirTables.Op(step.Parent!) == HirOp.Body;

    // The base constructor into the base subobject, then this layer's Field initializers (Initializers); a base whose clause Binding formed
    // no Type for (AdtDef.Base, as every Base step reads it) has no Place, so its base call, a part Binding rejected, is not placed.
    private void Construct(BoundType self, FunctionKoto constructor)
    {
        if (constructor.BaseInitializer is { } baseCall && AdtDef.Base(compilation.Types, self) is not null)
        {
            this.Initialize(this.body.Project(compilation.Types, new(0), MirProjectionKind.Base, 1), baseCall);
        }

        this.Initializers(self);
    }

    // SPEC 6.3.3: this layer's Field initializers in declaration order, each into its Field of _0 (Stored: of no Type for a Field whose own
    // slot failed, PLAN G1184); a part the instance does not store (Stored: past IMPL A.4's height or not substituted) has no Place, so its
    // initializer, a part Binding rejected, is not placed.
    private void Initializers(BoundType self)
    {
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
        if (!PublishedStep(node))
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
