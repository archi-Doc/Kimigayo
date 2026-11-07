// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.4, 8.6.1: inside a Contract, Self is a dedicated Type parameter, distinct from every reference to the
/// Contract; every associated-Type projection names its receiver and the Contract reference that declares it.</summary>
public class ContractSelfTest
{
    private const string Generic = "contract D<U>\n    associate N\n";
    private const string Needs = "func needs<V, W>(v: ref/V)\n    V is D<W>\n    ()\n";
    private const string Clause = Generic + "contract C\n    associate X is D<Self>\n";
    private const string Ancestor = Generic + "contract C: D<Self>\n";
    private const string Concrete = "struct S\n    Self is C\n    associate C.X is XI\nstruct XI\n    associate D.N is i32\n    Self is D<";
    private const string Refined = Generic + "contract C2<U>: D<U>\ncontract C\n";
    private const string Inner = Refined + "    func f<T>(value: T.(D<Self>).N) -> ()\n        T is C2<Self>\n";
    private const string Pair = "contract D2<U, W>\n    associate N\ncontract C2<U>: D2<Self, U>\ncontract C\n    func f<T>(value: T.(D2<T, ";

    private const string Argument = "contract E\ncontract D<U>\n    U is E\n" +
        "contract C\n    associate Y is E\n    associate Z is E\n    associate X is D<Self.Y>\n";

    private const string Expansion = "contract F<W>\ncontract D<U>\n    associate N is F<U>\ncontract C\n    associate Y\n    associate X is D<Self.Y>\n" +
        "func needs<V, W>(v: ref/V)\n    V is F<W>\n    ()\nfunc probe<T>(value: ref/T.(C).X.(D<T.(C).Y>).N)\n    T is C\n    needs<T.(C).X.(D<T.(C).Y>).N, ";

    [Fact]
    public void SelfIsADedicatedParameterAndProjectionsNameTheirContract()
    {
        const string Source = "public contract Holder\n    associate Item\n    func take(self: ref/Self) -> Item\n" +
            "func read<H>(holder: ref/H) -> H.Item\n    H is Holder\n    H.Item is Copy\n    return holder.take()\nlet x = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var holder = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Holder");
        var take = holder.Members.OfType<FunctionKoto>().Single();
        var self = take.Parameters[0].Type.TypeOf()!.Components[0];
        Assert.True(Binding.IsContractSelf(self));
        Assert.Equal(BoundTypeKind.Parameter, self.Kind);
        Assert.NotSame(holder.SymbolOf()!.Type, self);

        // Inside the Contract, Item is Self's Item declared by Holder; in a generic body, H's Item declared by Holder.
        Assert.Equal([self, holder.SymbolOf()!.Type!], take.ReturnType!.TypeOf()!.Components);
        var read = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "read");
        var projection = read.ReturnType!.TypeOf()!;
        Assert.Equal(BoundTypeKind.AssociatedProjection, projection.Kind);
        Assert.Same(read.GenericArguments[0].TypeOf(), projection.Components[0]);
        Assert.Same(holder.SymbolOf()!.Type, projection.Components[1]);
    }

    [Fact]
    public void AGenericContractNamesItsOwnParametersAndEachBoundReference()
    {
        const string Source = "func first<S>(items: ref/S) -> S.(Indexable<isize>).Element\n    S is Indexable<isize>\n    S.(Indexable<isize>).Element is Copy\n    return items[0]\nlet x = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var indexable = c.Binding.Library.Indexable!;
        var index = ((ContractKoto)indexable.Declaration).Members.OfType<FunctionKoto>().Single();
        var element = index.ReturnType!.TypeOf()!.Components[0];

        // `Element` of the requirement is Self's Element declared by Indexable<Key>, the Contract applied to its own Key.
        Assert.True(Binding.IsContractSelf(element.Components[0]));
        Assert.Equal(BoundTypeKind.Constructed, element.Components[1].Kind);
        Assert.Same(((ContractKoto)indexable.Declaration).GenericParameterNodes[0].TypeOf(), element.Components[1].Components[0]);

        var first = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "first");
        var projection = first.ReturnType!.TypeOf()!;
        Assert.Same(first.GenericArguments[0].TypeOf(), projection.Components[0]);
        Assert.Same(BoundType.ISize, projection.Components[1].Components[0]);
    }

    // SPEC 8.4: a Contract's Self inside a Contract reference's Type arguments is the conforming Type too (PLAN G180): in what a premise
    // `T is C` supplies through a clause, an ancestor and a refinement, in what an expansion of a supplied fact supplies, and in a concrete
    // conformance's verification. Each valid row (no code) has an invalid twin.
    [Theory]
    [InlineData(Clause + Needs + "func probe<T>(value: ref/T.(C).X)\n    T is C\n    needs<T.(C).X, T>(value)", "")]
    [InlineData(Clause + Needs + "func probe<T>(value: ref/T.(C).X)\n    T is C\n    needs<T.(C).X, i32>(value)", "UnprovenConstraint_Kd")]
    [InlineData(Argument + Needs + "func probe<T>(value: ref/T.(C).X)\n    T is C\n    needs<T.(C).X, T.(C).Y>(value)", "")]
    [InlineData(Argument + Needs + "func probe<T>(value: ref/T.(C).X)\n    T is C\n    needs<T.(C).X, T.(C).Z>(value)", "UnprovenConstraint_Kd")]
    [InlineData(Ancestor + "func probe<T>(value: T.(D<T>).N)\n    T is C\n    ()", "")]
    [InlineData(Ancestor + "func probe<T>(value: T.(D<i32>).N)\n    T is C\n    ()", "UnprovenConstraint_Kd")]
    [InlineData(Ancestor + Needs + "func probe<T>(value: ref/T)\n    T is C\n    needs<T, T>(value)", "")]
    [InlineData(Ancestor + Needs + "func probe<T>(value: ref/T)\n    T is C\n    needs<T, i32>(value)", "UnprovenConstraint_Kd")]
    [InlineData(Clause + "contract B: C\n" + Needs + "func probe<T>(value: ref/T.(C).X)\n    T is B\n    needs<T.(C).X, T>(value)", "")]
    [InlineData(Clause + "contract B: C\n" + Needs + "func probe<T>(value: ref/T.(C).X)\n    T is B\n    needs<T.(C).X, i32>(value)", "UnprovenConstraint_Kd")]
    [InlineData(Expansion + "T.(C).Y>(value)", "")]
    [InlineData(Expansion + "T>(value)", "UnprovenConstraint_Kd")]
    [InlineData(Clause + Concrete + "S>", "")]
    [InlineData(Clause + Concrete + "i32>", "UnsatisfiedConstraint_Kd")]

    // Inside a Contract, `T is C2<Self>` supplies C2's ancestors with T for C2's Self only; the enclosing Contract's Self, an argument, stays
    // (SPEC 8.3.2's refinement row), for a parameter, a clause, an associated Type and an ancestor naming both, and in implementation matching.
    [InlineData(Inner, "")]
    [InlineData(Refined + "    func f<T>(value: T.(D<T>).N) -> ()\n        T is C2<Self>\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd")]
    [InlineData(Refined + "    func f<T>(value: T) -> ()\n        T is C2<Self>\n        T.(D<Self>).N is i32\n", "")]
    [InlineData(Refined + "    func f<T>(value: T) -> ()\n        T is C2<Self>\n        T.(D<T>).N is i32\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd")]
    [InlineData(Refined + "    associate X is C2<Self>\n    Self.X.(D<Self>).N is i32\n", "")]
    [InlineData(Refined + "    associate X is C2<Self>\n    Self.X.(D<Self.X>).N is i32\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd")]
    [InlineData(Pair + "Self>).N) -> ()\n        T is C2<Self>\n", "")]
    [InlineData(Pair + "T>).N) -> ()\n        T is C2<Self>\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd")]
    [InlineData(Inner + "struct S\n    Self is C\n    public func f<T>(value: T.(D<S>).N) -> ()\n        T is C2<S>\n        ()", "")]
    [InlineData(
        Inner + "struct S\n    Self is C\n    public func f<T>(value: T.(D<S>).N) -> ()\n        T is C2<i32>\n        ()",
        "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd")]

    // Not G180: a concrete Type's ancestor path is keyed by the written `D<Self>` (PLAN G397, the identity unit after G170's C3).
    [InlineData(
        "contract D<U>\ncontract C: D<Self>\nstruct S\n    Self is C\n" + Needs + "func probe(s: ref/S)\n    needs<S, S>(s)", "NoApplicableOverload_Kd")]
    public void ContractReferenceArgumentsNameTheConformingType(string source, string codes)
    {
        var c = MinimalEmissionTest.Analyze(source + "\nlet x = 1");
        Assert.Equal(codes, string.Join(" ", c.Binding.Issues.Select(static x => x.Code.ToString())));
    }

    // A row is (unit, form, program, records). Its verdict comes from SPEC, never from a build: records is empty for a valid program, which
    // also generates, and otherwise lists each expected Error as `Code: source line` (see CompilationTestHelper.AssertRecordsAtLines). unit
    // guard: the row runs today and must keep passing. Any other unit is the plan's unit that makes the row pass: the row is Explicit
    // until that unit lands and removes the marker. `dotnet xUnitTest.dll -class <class> -explicit on` runs all of them.
    [Theory]
    // SPEC 8.4, 8.3.2's refinement row: a Contract's Self in a reference's arguments is the conforming Type; D's clause on its argument gives T.(C).X no
    // `k` (stale3).
    [InlineData("guard", "stale0", "contract D<U>\n    associate N\ncontract C\n    associate X is D<i32>\nfunc needs<V, W>(v: ref/V)\n    V is D<W>\n    ()\nfunc probe<T>(value: ref/T.(C).X)\n    T is C\n    needs<T.(C).X, i32>(value)\n", "")]
    [InlineData("guard", "stale3", "contract K\n    func k(self: ref/Self) -> i32\ncontract D<U>\n    U is K\ncontract C: K\n    associate X is D<Self>\nfunc probe<T>(value: ref/T.(C).X) -> i32\n    T is C\n    return value.k()\n", "*: return value.k()")]
    [InlineData("guard", "h6", "contract E\ncontract D<U>\n    U is E\n    associate N is D<Self.(D<U>).M>\n    associate M is E\ncontract C\n    associate X is D<Self.(C).Y>\n    associate Y is E\nfunc probe<T>(value: T.(C).X)\n    T is C\n    ()\n", "")]

    // SPEC 8.4, 8.3.1: `F<Self>` proves a clause on `Self`, and does not prove one on `Self.X` (cs1bad, whose resting records stay open).
    [InlineData("C2", "cs1", "contract F<W>\ncontract D<U>\n    associate N is F<U>\ncontract G<Z, W>\n    Z is F<W>\ncontract C\n    associate X is D<Self>\n    associate Q is G<Self.X.(D<Self>).N, Self>\n", "", Explicit = true)]
    [InlineData("guard", "cs1bad", "contract F<W>\ncontract D<U>\n    associate N is F<U>\ncontract G<Z, W>\n    Z is F<W>\ncontract C\n    associate X is D<Self>\n    associate Q is G<Self.X.(D<Self>).N, Self.X>\n", "UnprovenConstraint_Kd: associate Q is G<Self.X.(D<Self>).N, Self.X> | ...")]

    // SPEC 8.15.1, 8.3.1 (DECISIONS R4-M166): the clause of a declaration's own reference is required there and only its expansion would supply it, so
    // it is Unknown at the `associate`; the records resting on it stay open.
    [InlineData("guard", "exp1", "contract E\n    func e(self: ref/Self) -> i32\ncontract D<U>\n    U is E\ncontract C\n    associate Y\n    associate X is D<Self.Y>\nfunc probe<T>(value: ref/T.(C).Y) -> i32\n    T is C\n    return value.e()\n", "UnprovenConstraint_Kd: associate X is D<Self.Y> | ...")]
    [InlineData("guard", "exp2", "contract E\ncontract D<U>\n    U is E\ncontract C\n    associate Y\n    associate X is D<Self.Y>\nfunc needs<V>(v: ref/V)\n    V is E\n    ()\nfunc probe<T>(value: ref/T.(C).Y)\n    T is C\n    needs<T.(C).Y>(value)\n", "UnprovenConstraint_Kd: associate X is D<Self.Y> | ...")]
    [InlineData("guard", "exp2bad", "contract E\ncontract D<U>\n    U is E\ncontract C\n    associate Y\n    associate Z\n    associate X is D<Self.Y>\nfunc needs<V>(v: ref/V)\n    V is E\n    ()\nfunc probe<T>(value: ref/T.(C).Z)\n    T is C\n    needs<T.(C).Z>(value)\n", "UnprovenConstraint_Kd: associate X is D<Self.Y> | UnprovenConstraint_Kd: needs<T.(C).Z>(value) | ...")]

    // SPEC 8.4, 8.6.1: a concrete Type's ancestor reference names that Type and `D<i32>` is none of its paths (SPEC 8.3.2 Closed world). v2 replaces the
    // NoApplicableOverload_Kd pin of the theory above when its unit lands.
    [InlineData("C3", "v2", "contract D<U>\ncontract C: D<Self>\nstruct S\n    Self is C\nfunc needs<V, W>(v: ref/V)\n    V is D<W>\n    ()\nfunc probe(s: ref/S)\n    needs<S, S>(s)\n", "", Explicit = true)]
    [InlineData("guard", "v2bad", "contract D<U>\ncontract C: D<Self>\nstruct S\n    Self is C\nfunc needs<V, W>(v: ref/V)\n    V is D<W>\n    ()\nfunc probe(s: ref/S)\n    needs<S, i32>(s)\n", "*: needs<S, i32>(s)")]
    [InlineData("C3", "v3", "contract D<U>\n    associate N\ncontract C: D<Self>\nstruct S\n    Self is C\n    associate D<S>.N is i32\n", "", Explicit = true)]
    [InlineData("guard", "v3bad", "contract D<U>\n    associate N\ncontract C: D<Self>\nstruct S\n    Self is C\n    associate D<i32>.N is i32\n", "AssociatedTypeInferenceFailed_Kd: Self is C | InvalidAssociatedType_Kd: associate D<i32>.N is i32")]
    public void ADeclarationsContractReferenceNamesItsConformingType(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // An instantiated Contract reference is bound once and kept (boundContracts), so a warm rebind of the forms above allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(Expansion + "T.(C).Y>(value)")]
    [InlineData(Ancestor + Needs + "func probe<T>(value: ref/T)\n    T is C\n    needs<T, T>(value)")]
    [InlineData(Clause + Concrete + "S>")]
    public void WarmInstantiatedContractReferencesAllocateNothing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source + "\nlet x = 1");
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Instantiated Contract references failed.");
            }
        }));
    }
}
