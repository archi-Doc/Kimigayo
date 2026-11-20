// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly InferredArrayLengthRows inferredArrayLengths;
    private readonly ArrayInferenceShapeRows arrayInferenceShapes;

    private enum ArrayInferenceProblem : byte
    {
        Element,
        Length,
        LengthConflict,
        FixedArrayRequired,
    }

    // A negative length exists only in an initializer expectation, never in a completed Type. There is no inference
    // variable or inverse length solver: literal counts and complete result Types supply each written length hole.
    private static bool HasArrayLengthHole(BoundType type)
        => type.Kind == BoundTypeKind.FixedArray && (type.Length < 0 || HasArrayLengthHole(type.Components[0]));

    private static bool IsArrayHole(Koto syntax) => syntax is TypeSemanticsKoto { Type: null, Identifier: "_" };

    private static Koto ArrayShapeSyntax(Koto syntax)
    {
        while (true)
        {
            if (syntax is ParenthesizedTypeKoto parentheses)
            {
                syntax = parentheses.Type;
            }
            else if (syntax is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null, Type: { } inner })
            {
                syntax = inner;
            }
            else
            {
                return syntax;
            }
        }
    }

    // SPEC 4.3.1: a hole or dimension the initializer leaves undetermined or contradicts, with its closed reason, the syntax that supplies or
    // requires the evidence (Related) and, for a conflict, the sides it compared: a dimension's two lengths, or the Type found where the
    // annotation requires a fixed array.
    private void RecordArrayInference(Koto node, ArrayInferenceProblem reason, Koto related, ReadOnlySpan<ProblemFact> sides)
    {
        Span<ProblemFact> facts = [new(ProblemFactKind.Reason, SourceRef.Of(node), (long)reason), new(ProblemFactKind.Related, this.FactSite(related), 0), default, default];
        sides.CopyTo(facts[2..]);
        this.Record(node, BindingFailure.ArrayAnnotationInference, facts[..(2 + sides.Length)]);
    }

    private Koto? MissingArrayElementCause(ArrayLiteralKoto literal)
    {
        if (!this.arrayInferenceShapes.TryGetValue(literal, out var shape))
        {
            return null;
        }

        while (shape is FixedArrayTypeKoto array)
        {
            shape = ArrayShapeSyntax(array.ElementType);
        }

        return IsArrayHole(shape) && shape.BindingState == BindingState.Invalid ? shape : null;
    }

    private BoundType? BindArrayFill(ArrayLiteralKoto fill, BindingScope scope, BoundType? expected)
    {
        var length = fill.FillCount = this.BindLength(fill.FillLength!, scope);
        var element = this.BindNode(fill.Elements[0], scope, expected?.Kind == BoundTypeKind.FixedArray ? expected.Components[0] : null);
        if (length is not null && element is null && this.Deferred(fill.Elements[0]))
        {
            return Complete(fill, null); // SPEC 14.9.2 step 3 (DECISIONS BU-43): its element waits for its position's Type, and so does the fill.
        }

        if (length is null || element is null)
        {
            // A failed element or length explains the fill (SPEC 23.1.2); a length syntax that binds but is no length keeps
            // the formation failure.
            var failed = element is null && fill.Elements[0].BindingState != BindingState.Resolved ? fill.Elements[0]
                : length is null && fill.FillLength!.BindingState != BindingState.Resolved ? fill.FillLength : null;
            return failed is not null ? this.CompleteDependent(fill, failed) : this.Fail(fill, BindingFailure.InvalidTypeFormation);
        }

        if (this.ProveCopy(element, fill) != ConstraintProof.Proven)
        {
            this.FailConstraint(fill);
            return null;
        }

        var type = this.types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [element], length.IsConstant ? length.Value : 0, lengthExpression: length.IsConstant ? null : length);
        return Complete(fill, type);
    }

    // As for Tuple literals, retain the acquired element Type until the enclosing
    // declaration has inferred omitted Origins. Replacing it with the expectation
    // here would erase the very evidence needed by InferLocalOrigins.
    private BoundType? BindContextualArrayLiteral(ArrayLiteralKoto literal, BindingScope scope, BoundType expected)
    {
        BoundType? element = null;
        var complete = true;
        for (var i = 0; i < literal.Elements.Count; i++)
        {
            this.PropagateBound(literal.Elements[i], expected.Components[0]);
            var actual = this.BindNode(literal.Elements[i], scope, expected.Components[0]);
            if (actual is null)
            {
                complete = false;
            }
            else if (!ReferenceEquals(actual, BoundType.Never))
            {
                var failure = default(ComparisonFailure);
                var common = element is null ? actual : this.CommonOriginType(element, actual, out failure);
                if (common is null)
                {
                    this.FailMismatch(literal.Elements[i], literal.Elements[i], actual, element!, failure);
                    complete = false;
                }
                else
                {
                    element = common;
                }
            }
        }

        if (expected.Kind == BoundTypeKind.FixedArray && (expected.LengthExpression is not null || (expected.Length >= 0 && expected.Length != literal.Elements.Count)))
        {
            return this.Fail(literal, BindingFailure.TypeMismatch);
        }

        return Complete(literal, complete ? this.types.Intern(expected.Kind, expected.Symbol, expected.Semantics, [element ?? expected.Components[0]], expected.Kind == BoundTypeKind.FixedArray ? literal.Elements.Count : expected.Length) : null);
    }

    private void InferArrayAnnotation(Koto syntax, Koto initializer, BindingScope scope)
    {
        var hole = ArrayShapeSyntax(syntax);
        if (hole is not FixedArrayTypeKoto)
        {
            return;
        }

        var lengthHole = false;
        while (hole is FixedArrayTypeKoto array)
        {
            lengthHole |= IsArrayHole(array.Length);
            hole = ArrayShapeSyntax(array.ElementType);
        }

        if (!lengthHole && !IsArrayHole(hole))
        {
            return;
        }

        BoundType? established = null;
        BoundType? literalDefault = null;
        var gathered = this.ArrayElementEvidence(syntax, initializer, scope, ref established, ref literalDefault, out var failed);
        if (IsArrayHole(hole))
        {
            if (gathered && (established ?? literalDefault) is { } element)
            {
                Complete(hole, element);
            }
            else
            {
                if (failed is not null)
                {
                    this.CompleteDependent(hole, failed);
                }
                else
                {
                    this.RecordArrayInference(hole, ArrayInferenceProblem.Element, initializer, []);
                }
            }
        }

        for (var shape = ArrayShapeSyntax(syntax); shape is FixedArrayTypeKoto array; shape = ArrayShapeSyntax(array.ElementType))
        {
            if (IsArrayHole(array.Length) && !this.inferredArrayLengths.ContainsKey(array.Length))
            {
                if (failed is not null)
                {
                    this.CompleteDependent(array.Length, failed);
                }
                else
                {
                    this.RecordArrayInference(array.Length, ArrayInferenceProblem.Length, initializer, []);
                }
            }
        }
    }

    // Only a known leaf supplies an expectation. `_` alone does not demand another fixed dimension or select an element
    // Type for an otherwise unconstrained generic call. Ordinary candidate inference receives the written structure.
    private BoundType? ArrayAnnotationExpectation(Koto shape, BindingScope scope, BoundType? inferredElement = null)
    {
        shape = ArrayShapeSyntax(shape);
        if (shape is not FixedArrayTypeKoto array)
        {
            return IsArrayHole(shape) ? inferredElement : this.BindType(shape, scope);
        }

        if (this.ArrayAnnotationExpectation(array.ElementType, scope, inferredElement) is not { } element)
        {
            return null;
        }

        var length = IsArrayHole(array.Length) ? this.inferredArrayLengths.GetValueOrDefault(array.Length) : this.BindLength(array.Length, scope);
        return this.types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [element], length is null ? -1 : length.IsConstant ? length.Value : 0, lengthExpression: length is { IsConstant: false } ? length : null);
    }

    private bool InferArrayLength(FixedArrayTypeKoto array, BoundLength length, Koto source)
    {
        if (!IsArrayHole(array.Length))
        {
            return true;
        }

        if (length.IsConstant && length.Value < 0)
        {
            return true; // An empty contextual literal with an unknown nested dimension supplies no length evidence.
        }

        if (this.inferredArrayLengths.TryGetValue(array.Length, out var previous) && !ReferenceEquals(previous, length))
        {
            var site = SourceRef.Of(source);
            this.RecordArrayInference(source, ArrayInferenceProblem.LengthConflict, array.Length, [new(ProblemFactKind.Actual, site, this.report.Keep(length)), new(ProblemFactKind.Expected, site, this.report.Keep(previous))]);
            return false;
        }

        this.inferredArrayLengths[array.Length] = length;
        Complete(array.Length, BoundType.ISize);
        return true;
    }

    private BoundType CompleteArrayExpectation(BoundType expected, BoundType actual)
    {
        if (!HasArrayLengthHole(expected) || actual.Kind != BoundTypeKind.FixedArray)
        {
            return expected;
        }

        var element = this.CompleteArrayExpectation(expected.Components[0], actual.Components[0]);
        return this.types.Intern(BoundTypeKind.FixedArray, null, SemanticsKind.Owner, [element], expected.Length < 0 ? actual.Length : expected.Length, lengthExpression: expected.Length < 0 ? actual.LengthExpression : expected.LengthExpression);
    }

    // SPEC 4.3.1, 10.2 (PLAN G1116): whether a list literal without a fixed-array or Array expectation takes its own Type by the place
    // rule now (BindIndependentArrayLiteral), the position's fit judging that Type as it judges a fill's or a Dictionary literal's.
    // Two places decide its Type where no implemented rule does, and keep the located Unsupported_Kd (the owner's O-449 (a)): an abstract
    // expectation (BoundType.IsAbstract: a Type parameter, a projection or a pair term, a pair layer or a pair target, still abstract
    // after the scope's identity premises are substituted), which has no form for SPEC 4.3.1 to take the literal's from (8.2.3: an
    // abstract Type takes the form its binding, a completed projection or its admitted set fixes, which the fit does not resolve); and,
    // for an empty literal, a place decided later (SPEC 4.3.1 Places, 14.9.2 step 3), where only parts publish a literal as waiting
    // (AwaitsFor), so an empty one would wait with nothing to bind it. PLAN G1123 removes both clauses: its (a) publishes an empty
    // literal's wait; its (b), with a pair target's identity premises substituted before this dispatch as a Type parameter's are,
    // leaves abstract only an expectation with no array form, which the place rule and the fit then judge.
    private bool TypedByPlaceRule(ArrayLiteralKoto literal, BoundType? expected)
        => expected?.IsAbstract != true && (literal.Elements.Count != 0 || !this.DecidedLater(literal));

    // SPEC 4.3: without a fixed-array or Array expectation (none, or a constructed Type no array literal has, whose fit then fails at
    // the position; TypedByPlaceRule) an independent literal constructs an Array whose element Type is the
    // one complete Type its elements establish; unfitted numeric literals follow ordinary inference. Call arguments keep
    // candidate-local fitting and never take this default. While it collects that evidence, its parts' literals wait for it. Every
    // element is bound, also after a failed one (SPEC 23.1.2), as a Dictionary literal's parts are (IndependentDictionaryType).
    private BoundType? BindIndependentArrayLiteral(ArrayLiteralKoto literal, BindingScope scope)
    {
        // SPEC 10.5.1 ¶1-2, 14.9.2 step 3 (PLAN G924 B2): under a call's argument probe the literal waits for its position, its elements
        // bound on their own: they meet at the call's places (WholeEvidence), never as an independent literal's elements.
        if (this.UnderArgumentProbe(literal) && this.DecidedLater(literal))
        {
            foreach (var part in literal.Elements)
            {
                if (!IsUnfittedLiteral(KotoHelper.UnwrapParentheses(part)))
                {
                    _ = this.BindNode(part, scope);
                }
            }

            return Complete(literal, null);
        }

        BoundType? established = null;
        BoundType? literalDefault = null;
        var collecting = this.collectingElements;
        this.collectingElements = literal;
        var collected = true;
        for (var i = 0; i < literal.Elements.Count; i++)
        {
            collected &= this.CollectLiteralElementEvidence(literal.Elements[i], scope, ref established, ref literalDefault);
        }

        this.collectingElements = collecting;
        // SPEC 14.9.2 step 3, 4.3: no element establishes a Type, so the literal waits for its position's.
        if (collected && established is null && this.DecidedLater(literal))
        {
            return Complete(literal, null);
        }

        // Without an established Type, or when an element failed (SPEC 23.1.2: the others are still bound), the waiting parts default.
        for (var i = 0; (!collected || established is null) && i < literal.Elements.Count; i++)
        {
            this.DefaultWaitingPart(literal.Elements[i], ref literalDefault);
        }

        if (!collected)
        {
            return Complete(literal, null);
        }

        if ((established ?? literalDefault) is not { } element)
        {
            return this.Fail(literal, BindingFailure.MissingType, true);
        }

        for (var i = 0; i < literal.Elements.Count; i++)
        {
            this.RequireAtPosition(literal.Elements[i], scope, element);
        }

        return Complete(literal, this.types.Intern(BoundTypeKind.Array, this.Library.DynamicArray, SemanticsKind.Owner, [element]));
    }

    // Arrays and Dictionary keys/values use the same bounded evidence rule: complete Types establish the expectation;
    // unfitted literals supply only a default, and Never contributes no value Type. No temporary element list is needed.
    private bool CollectLiteralElementEvidence(Koto source, BindingScope scope, ref BoundType? established, ref BoundType? literalDefault)
    {
        var unwrapped = KotoHelper.UnwrapParentheses(source);
        if (IsUnfittedLiteral(unwrapped))
        {
            literalDefault ??= this.LiteralDefault(unwrapped);
            return true;
        }

        var actual = this.BindNode(source, scope);
        if (actual is null)
        {
            return this.Deferred(source); // SPEC 4.3, 14.9.2 step 3: a part waiting for the element Type supplies none; it is bound at it.
        }

        if (ReferenceEquals(actual, BoundType.Never))
        {
            return true;
        }

        if (this.AwaitsPosition(source))
        {
            // SPEC 14.9.2 step 3, 4.3: it takes the established element Type; without one, the awaiting elements' own common Type.
            literalDefault = literalDefault is null ? actual : this.CommonOriginType(literalDefault, actual, out _) ?? literalDefault;
            return true;
        }

        var failure = default(ComparisonFailure);
        var common = established is null ? actual : this.CommonOriginType(established, actual, out failure);
        if (common is null)
        {
            this.FailMismatch(source, source, actual, established!, failure);
            return false;
        }

        established = common;
        return true;
    }

    private bool ArrayElementEvidence(Koto shape, Koto source, BindingScope scope, ref BoundType? established, ref BoundType? literalDefault, out Koto? failed)
    {
        failed = null;
        shape = ArrayShapeSyntax(shape);
        source = KotoHelper.UnwrapParentheses(source);
        if (shape is FixedArrayTypeKoto array && source is ArrayLiteralKoto literal)
        {
            if (literal.Elements.Count == 0)
            {
                this.arrayInferenceShapes[literal] = shape;
            }

            var length = literal.FillLength is { } fill ? this.BindLength(fill, scope) : this.InternLength(LengthOperation.Constant, literal.Elements.Count);
            if (length is null || !this.InferArrayLength(array, length, source))
            {
                failed = length is null ? literal.FillLength : source;
                return false;
            }

            for (var i = 0; i < literal.Elements.Count; i++)
            {
                if (!this.ArrayElementEvidence(array.ElementType, literal.Elements[i], scope, ref established, ref literalDefault, out failed))
                {
                    return false;
                }
            }

            return true;
        }

        if (shape is not FixedArrayTypeKoto && !IsArrayHole(shape))
        {
            return true; // Ordinary contextual Binding will fit known leaves after all lengths are established.
        }

        if (IsArrayHole(shape) && IsUnfittedLiteral(source))
        {
            literalDefault ??= this.LiteralDefault(source);
            return true;
        }

        var expectation = this.ArrayAnnotationExpectation(shape, scope);
        var target = source is LabeledKoto label ? label.Target : source;
        if (expectation is null && shape is FixedArrayTypeKoto && target is not TryKoto && target is IfKoto or MatchKoto or DoKoto or LoopKoto)
        {
            // Preserve written dimensions through result-producing syntax even when its leaf Type is a hole. Result
            // inference gathers the same array evidence after Pattern bindings exist, then fits the ordinary bodies.
            this.arrayInferenceShapes[target] = shape;
        }

        var actual = this.BindNode(source, scope, expectation);
        if (actual is null)
        {
            failed = source;
            return false;
        }

        if (ReferenceEquals(actual, BoundType.Never))
        {
            return true;
        }

        // A typed initializer contributes its complete element Type. Dimensions
        // are checked by ordinary fitting after the annotation is complete.
        while (shape is FixedArrayTypeKoto nested)
        {
            if (actual.Kind != BoundTypeKind.FixedArray)
            {
                var site = SourceRef.Of(source);
                this.RecordArrayInference(source, ArrayInferenceProblem.FixedArrayRequired, nested, [new(ProblemFactKind.Actual, site, this.report.Keep(actual)), new(ProblemFactKind.ExpectedForm, site, (long)MismatchForm.FixedArray)]);
                failed = source;
                return false;
            }

            if (!this.InferArrayLength(nested, actual.LengthExpression ?? this.InternLength(LengthOperation.Constant, actual.Length), source))
            {
                failed = source;
                return false;
            }

            actual = actual.Components[0];
            shape = ArrayShapeSyntax(nested.ElementType);
        }

        if (!IsArrayHole(shape))
        {
            return true;
        }

        var failure = default(ComparisonFailure);
        var common = established is null ? actual : this.CommonOriginType(established, actual, out failure);
        if (common is null)
        {
            this.FailMismatch(source, source, actual, established!, failure);
            failed = source;
            return false;
        }

        established = common;
        return true;
    }

    /// <summary>The `inferredArrayLengths` family's one accessor (DECISIONS BU-220): a pass family, the length an initializer supplied for
    /// a written length hole `_` (SPEC 4.3.1), kept in the hole's module's NodeColumns at its SyntaxId (null for none). Its members keep the
    /// names its readers use.</summary>
    private readonly struct InferredArrayLengthRows(Binding binding)
    {
        internal BoundLength this[Koto hole]
        {
            set => HirTables.Of(hole).Families.InferredArrayLengths.Ref(hole.SyntaxId, binding) = value;
        }

        internal bool ContainsKey(Koto hole) => this.GetValueOrDefault(hole) is not null;

        internal bool TryGetValue(Koto hole, [NotNullWhen(true)] out BoundLength? length) => (length = this.GetValueOrDefault(hole)) is not null;

        internal BoundLength? GetValueOrDefault(Koto hole)
            => hole.SyntaxOwner is { } table ? table.Hir.Families.InferredArrayLengths.Get(hole.SyntaxId, binding) : null;
    }

    /// <summary>The `arrayInferenceShapes` family's one accessor (DECISIONS BU-220): a pass family, the written fixed-array shape an empty
    /// literal or a result-producing target takes its array evidence from (SPEC 4.3.1, 14.9.2), kept in the target's module's NodeColumns
    /// at its SyntaxId (null for none). Its members keep the names its readers use.</summary>
    private readonly struct ArrayInferenceShapeRows(Binding binding)
    {
        internal Koto this[Koto target]
        {
            set => HirTables.Of(target).Families.ArrayInferenceShapes.Ref(target.SyntaxId, binding) = value;
        }

        internal bool TryGetValue(Koto target, [NotNullWhen(true)] out Koto? shape)
            => (shape = target.SyntaxOwner is { } table ? table.Hir.Families.ArrayInferenceShapes.Get(target.SyntaxId, binding) : null) is not null;
    }
}
