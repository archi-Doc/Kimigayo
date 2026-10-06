// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private static BoundType? ValueType(OwnershipBody body, int id)
    {
        var place = ValuePlace(body.Operations[id]);
        return place >= 0 ? body.Places[place].Type : null;
    }

    private static int ValuePlace(OwnershipOperation operation) => operation.Kind is OwnershipOperationKind.Consume or OwnershipOperationKind.AcquirePattern or OwnershipOperationKind.Borrow or OwnershipOperationKind.WriteElement ||
        (operation.Kind == OwnershipOperationKind.Read && operation.Input >= 0) ? operation.Input : operation.Place;

    private static bool ValidateValues(OwnershipBody body, BodyLowering? lowering)
    {
        if (body.Values.Count != body.Operations.Count)
        {
            return false;
        }

        if (body.Identities is { } identities)
        {
            foreach (var identity in identities)
            {
                var source = identity.Source;
                // A transfer (@move) and a Copy (@copy) designate their acquired input like Identity Acquisition, for every
                // acquired Type; the Type targets of Identity Acquisition keep their supported set. An operand read through its
                // reference layers (SPEC 13.5.2) supplies the read Type.
                var left = OperandType(source);
                var context = (uint)identity.Place < (uint)body.Places.Count ? body.Places[identity.Place].DefaultContext : -1;
                if ((uint)identity.Place >= (uint)body.Places.Count || source.ConversionBinding is not (ConversionBinding.Identity or ConversionBinding.Transfer) ||
                    body.Concrete(body.SubstituteDefaultType(source.BoundType, context)) is not { } type ||
                    (source.ConversionBinding == ConversionBinding.Identity && !Binding.SupportsIdentityAcquisition(type) && !Binding.IsCopyOperation(source)) ||
                    !ReferenceEquals(type, body.Concrete(body.SubstituteDefaultType(left, context))) || !ReferenceEquals(type, body.Concrete(body.SubstituteDefaultType(source.Right.BoundType, context))) ||
                    !ReferenceEquals(type, body.Places[identity.Place].Type))
                {
                    return false;
                }
            }
        }

        for (var id = 0; id < body.Values.Count; id++)
        {
            var value = body.Values[id];
            var expected = value.Kind switch
            {
                OwnershipValueKind.None or OwnershipValueKind.Constant or OwnershipValueKind.Parameter or OwnershipValueKind.Call or OwnershipValueKind.DefaultCall or OwnershipValueKind.DefaultRead or OwnershipValueKind.StringComparison or OwnershipValueKind.Borrow or OwnershipValueKind.Element or OwnershipValueKind.PatternProjection or OwnershipValueKind.StaticRead => 0,
                OwnershipValueKind.Alias or OwnershipValueKind.Unary or OwnershipValueKind.Convert or OwnershipValueKind.BorrowedField or OwnershipValueKind.PointerLoad or OwnershipValueKind.ContractComparison or OwnershipValueKind.RuntimeTypeTest => 1,
                OwnershipValueKind.ClosureErasure => value.Count is 0 or 1 ? value.Count : -1, // A Function Item has no source closure.
                OwnershipValueKind.Binary or OwnershipValueKind.BorrowedFieldWrite or OwnershipValueKind.BorrowedUpdate => 2,
                OwnershipValueKind.PointerStore => IsScalar(ValueType(body, id)!) ? 2 : 1,
                OwnershipValueKind.Address => value.Count is >= 0 and <= 2 ? value.Count : -1,
                OwnershipValueKind.PointerProject => value.Count is 1 or 2 ? value.Count : -1,
                OwnershipValueKind.Sequence => value.Count is 0 or 1 ? value.Count : -1,
                OwnershipValueKind.Formatting => value.Count is 0 or 1 ? value.Count : -1,
                OwnershipValueKind.DictionaryLiteral => body.Operations[id].Kind == OwnershipOperationKind.CheckDictionaryKey ? 1 : 2,
                OwnershipValueKind.Phi or OwnershipValueKind.Closure => value.Count,
                OwnershipValueKind.Capture => 0,
                _ => -1,
            };
            var length = value.Kind == OwnershipValueKind.Phi ? body.PhiInputs.Count : body.ValueOperands.Count;
            if (value.Count < 0 || value.Count != expected || value.Start < 0 || value.Start > length - value.Count)
            {
                return false;
            }

            // Non-scalar operations retain ownership-only Place flow until their lowering is implemented.
            var operation = body.Operations[id];
            if (operation.Kind == OwnershipOperationKind.WriteElement &&
                ((uint)operation.Place >= (uint)body.Places.Count || (uint)operation.Input >= (uint)body.Places.Count))
            {
                return false;
            }

            var scalar = IsScalar(ValueType(body, id)!);
            var pointerAccess = value.Kind is OwnershipValueKind.PointerLoad or OwnershipValueKind.PointerStore;
            if (value.Kind == OwnershipValueKind.Convert && !scalar)
            {
                return false;
            }

            if (scalar && operation.Kind == OwnershipOperationKind.Branch && value.Kind != OwnershipValueKind.Phi)
            {
                return false;
            }

            if (value.Kind == OwnershipValueKind.Phi && (!scalar || operation.Kind != OwnershipOperationKind.Branch ||
                !ReferenceEquals(ValueType(body, id), body.ConcreteAt(operation.Source.BoundType, id))))
            {
                return false;
            }

            if (!scalar && !pointerAccess && operation.Kind != OwnershipOperationKind.Branch)
            {
                continue;
            }

            for (var n = 0; n < value.Count; n++)
            {
                var input = Input(body, id, n);
                if ((uint)input >= (uint)body.Values.Count || (value.Kind != OwnershipValueKind.Phi && input >= id) ||
                    !IsScalar(ValueType(body, input)!))
                {
                    return false;
                }

                var producer = body.Operations[input].Kind;
                if (producer is not (OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Produce or OwnershipOperationKind.Call or OwnershipOperationKind.InitializeSubject or OwnershipOperationKind.AcquirePattern or OwnershipOperationKind.Borrow or OwnershipOperationKind.UpdateBorrowed) && body.Values[input].Kind != OwnershipValueKind.Phi)
                {
                    return false;
                }

                if (value.Kind == OwnershipValueKind.Phi && (body.Values[input].Kind == OwnershipValueKind.Alias || !FitsValue(ValueType(body, input), ValueType(body, id), operation.Source)))
                {
                    return false;
                }

                if (value.Kind == OwnershipValueKind.Alias && !FitsValue(ValueType(body, input), operation.Kind == OwnershipOperationKind.Branch ? BoundType.Boolean : ValueType(body, id), operation.Source))
                {
                    return false;
                }
            }

            if (value.Kind == OwnershipValueKind.Call && operation.Kind != OwnershipOperationKind.Call)
            {
                return false;
            }

            if (value.Kind == OwnershipValueKind.Convert &&
                (operation.Kind != OwnershipOperationKind.Produce ||
                (value.Constant == OwnershipValue.PositionConversion
                    ? !IsPositionConversion(operation.Source, ValueType(body, Input(body, id, 0)), ValueType(body, id))
                    : value.Constant == OwnershipValue.RawPlaceBorrow
                    ? !ReferenceTypes.IsPointer(ValueType(body, Input(body, id, 0))) || !ReferenceTypes.IsReference(ValueType(body, id))
                    : operation.Source is not Parsing.ConversionKoto conversion ||
                        !ReferenceEquals(ValueType(body, id), SignatureType(lowering, conversion.BoundType)) ||
                        !ReferenceEquals(ValueType(body, Input(body, id, 0)), SignatureType(lowering, OperandType(conversion))) ||
                        !ValidScalarConversion(conversion.ConversionBinding, ValueType(body, Input(body, id, 0)), ValueType(body, id)))))
            {
                return false;
            }

            if (value.Kind == OwnershipValueKind.Parameter &&
                (operation.Kind != OwnershipOperationKind.Produce || body.Places[operation.Place].Kind != OwnershipPlaceKind.Parameter ||
                value.Constant < 0 || value.Constant >= body.ParameterCount ||
                !ReferenceEquals(operation.Source, body.Function.Parameters[(int)value.Constant].Type) ||
                !ReferenceEquals(ValueType(body, id), SignatureType(lowering, body.Function.Parameters[(int)value.Constant].Type.BoundType))))
            {
                return false;
            }

            if (value.Kind == OwnershipValueKind.Constant)
            {
                var type = ValueType(body, id);
                if (operation.Source.BoundSymbol?.Property is { } property)
                {
                    if (!StaticScalar.TryGet(property, out var literal) || !ReferenceEquals(type, property.Type) || literal != value.Constant)
                    {
                        return false;
                    }

                    continue;
                }

                if (FloatingTypes.Supports(type) || FloatingTypes.Supports(SignatureType(lowering, operation.Source.BoundType)))
                {
                    // SPEC 13.5.4.2: a direct literal converted at compile time carries its folded bits.
                    var folded = operation.Source is Parsing.ConversionKoto { FoldedConstant: { } constant } ? constant : (Int128?)null;
                    if (!ReferenceEquals(type, SignatureType(lowering, operation.Source.BoundType)) ||
                        (folded is null ? !FloatingTypes.TryLiteral(operation.Source, out var bits) || bits != value.Constant : folded != value.Constant))
                    {
                        return false;
                    }

                    continue;
                }

                if (ReferenceEquals(type, BoundType.Char) || operation.Source is Parsing.CharLiteralKoto)
                {
                    if (!ReferenceEquals(type, BoundType.Char) || !ReferenceEquals(SignatureType(lowering, operation.Source.BoundType), BoundType.Char) ||
                        operation.Source is not Parsing.CharLiteralKoto { Value: { } character } ||
                        !ScalarTypes.IsCharacterValue(value.Constant) || value.Constant != character.Value)
                    {
                        return false;
                    }

                    continue;
                }

                if (ReferenceTypes.IsPointer(type) || operation.Source is Parsing.NullLiteralKoto)
                {
                    // SPEC 5.1: null is the only pointer literal.
                    if (!ReferenceTypes.IsPointer(type) || !ReferenceEquals(type, SignatureType(lowering, operation.Source.BoundType)) || operation.Source is not Parsing.NullLiteralKoto || value.Constant != 0)
                    {
                        return false;
                    }

                    continue;
                }

                var width = ScalarTypes.Width(type);
                if (ReferenceEquals(type, BoundType.Boolean) ? value.Constant < 0 || value.Constant > 1 : width == 0 || ScalarTypes.Normalize(value.Constant, width) != value.Constant)
                {
                    return false;
                }

                if (width == 128)
                {
                    var source = operation.Source;
                    var number = source is Parsing.PrefixMinusKoto or Parsing.PrefixPlusKoto
                        ? ((Parsing.UnaryKoto)source).Operand as Parsing.NumberLiteralKoto : source as Parsing.NumberLiteralKoto;
                    if (source is Parsing.ConversionKoto { FoldedConstant: { } folded })
                    {
                        // SPEC 13.5.4.2: a direct literal converted at compile time carries its folded payload.
                        if (folded != value.Constant || !ReferenceEquals(type, SignatureType(lowering, source.BoundType)))
                        {
                            return false;
                        }
                    }
                    else if (number is not null)
                    {
                        if (!ReferenceEquals(type, SignatureType(lowering, source.BoundType)) || !number.TryGetIntegerMagnitude(out var magnitude) ||
                            !ScalarTypes.TryLiteral(type, magnitude, source is Parsing.PrefixMinusKoto, 64, out var bits) || bits != value.Constant)
                        {
                            return false;
                        }
                    }
                    else if (value.Constant != 1 || source.Akind is not (Parsing.KotoKind.PrefixPlusPlus or Parsing.KotoKind.PrefixMinusMinus or Parsing.KotoKind.PostfixIncrement or Parsing.KotoKind.PostfixDecrement))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    // Origin restriction changes lifetime contracts, never storage, including nested
    // reference components. Require both physical agreement and the semantic proof.
    // A value fits its destination's storage and Origin contract. An Origin omitted in an initializer's Type expression is
    // inferred after that Type was formed (SPEC 15.4.4); Binding proves its relations at the BodyOrigins deadline, so its
    // unresolved atom is not compared here.
    private static bool FitsValue(BoundType? source, BoundType? target, Parsing.Koto? use = null)
        => ReferenceEquals(source, target) ||
            (ReferenceTypes.StorageMatches(source, target) && (Binding.FitsType(source!, target!) ||
                (use is not null && use.CodeContext.Compilation.Binding.FitsVerifiedTypeAt(source!, target!, use)) || HasInferenceOrigin(source!) || HasInferenceOrigin(target!)));

    private static bool HasInferenceOrigin(BoundType type)
    {
        if (type.Origin?.Kind == OriginKind.Inference)
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (type.OriginArguments[i].Kind == OriginKind.Inference)
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasInferenceOrigin(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 4.6.4, 4.6.9: an element key, a directly applied range boundary or a written `^` operand of another integer Type
    // becomes an isize position after it is evaluated; a value isize cannot hold becomes -1.
    private static bool IsPositionConversion(Parsing.Koto source, BoundType? input, BoundType? target)
        => ReferenceEquals(target, BoundType.ISize) && ScalarTypes.Width(input) != 0 && !ReferenceEquals(input, BoundType.ISize) &&
        (source.Parent is Parsing.RangeKoto or Parsing.FromEndIndexKoto || (source.Parent is Parsing.IndexKoto index && ReferenceEquals(index.Right, source)));

    // SPEC 13.5.2: an operand read through its reference layers supplies the read Type to its conversion; an address converts
    // the borrow of its operand (SPEC 5.4).
    private static BoundType? OperandType(Parsing.ConversionKoto conversion)
        => conversion.ConversionBinding == ConversionBinding.Address ? conversion.Right.BoundType :
            conversion.CodeContext.Compilation.Binding.TryGetAdaptation(conversion.Left, out var read) && read.Kind == ExpectedAdaptationKind.ReferentRead ? read.Type : conversion.Left.BoundType;

    private static bool ValidScalarConversion(ConversionBinding binding, BoundType? source, BoundType? target)
        => binding == ConversionBinding.Pointer ? (ReferenceTypes.IsPointer(source) && (ReferenceTypes.IsPointer(target) || ReferenceEquals(target, BoundType.USize))) || (ReferenceEquals(source, BoundType.USize) && ReferenceTypes.IsPointer(target)) :
            binding == ConversionBinding.Address ? ReferenceTypes.IsReference(source) && ReferenceTypes.IsPointer(target) :
            binding is ConversionBinding.Integer or ConversionBinding.Wrap ? ScalarTypes.Width(source) != 0 && ScalarTypes.Width(target) != 0 :
            binding == ConversionBinding.Bits ? FloatingTypes.Supports(source) != FloatingTypes.Supports(target) &&
                (FloatingTypes.Supports(source) ? ScalarTypes.Width(target) : ScalarTypes.Width(source)) == (ReferenceEquals(FloatingTypes.Supports(source) ? source : target, BoundType.F32) ? 32 : 64) :
            binding == ConversionBinding.Floating ? FloatingTypes.Supports(source) && FloatingTypes.Supports(target) :
            binding == ConversionBinding.Numeric &&
            ((FloatingTypes.Supports(source) && ScalarTypes.Width(target, 64) is > 0 and <= 64) ||
            (FloatingTypes.Supports(target) && ScalarTypes.Width(source, 64) is > 0 and <= 64));

    private bool FitsStoredValue(OwnershipPlace source, OwnershipPlace target, Parsing.Koto use)
    {
        if (FitsValue(source.Type, target.Type, use))
        {
            return true;
        }

        // A generic body's published premises prove its transfer before substitution. Caller-local Origins need not
        // be related in that declaration's scope; retain the proof only for the exact substituted storage Types.
        var declaredSource = source.Source.BoundType;
        var declaredTarget = target.Kind == OwnershipPlaceKind.Result && target.Source is Parsing.FunctionKoto function
            ? function.ReturnType?.BoundType : target.Source.BoundType;
        return this.instance is not null && declaredSource is not null && declaredTarget is not null &&
            ReferenceEquals(SignatureType(this, declaredSource), source.Type) && ReferenceEquals(SignatureType(this, declaredTarget), target.Type) &&
            ReferenceTypes.StorageMatches(source.Type, target.Type) && this.instanceBinding!.FitsVerifiedTypeAt(declaredSource, declaredTarget, use);
    }
}
