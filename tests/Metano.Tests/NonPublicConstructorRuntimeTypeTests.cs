using Metano.Tests.TreeShaking;

namespace Metano.Tests;

/// <summary>
/// A class whose constructors are all non-public still gets a <c>$runtimeType</c> descriptor
/// that names the class itself as <c>ctor</c> through a type-only <c>as never</c> (the slot is
/// typed as a public constructor, TS2322 otherwise), with the constructor visibility intact.
/// A class with a public constructor keeps the plain reference (#827).
/// </summary>
public class NonPublicConstructorRuntimeTypeTests
{
    private const string Source = """
        [assembly: TranspileAssembly]
        [assembly: EmitPackage("probe-pkg")]

        public sealed class Prop<T>
        {
            private Prop(T value) { Value = value; }
            public T Value { get; }
            public static implicit operator Prop<T>(T value) => new(value);
        }

        public class ProtectedOnly
        {
            protected ProtectedOnly() { }
            public static ProtectedOnly Make() => new ProtectedOnly();
        }

        public abstract class AbstractProtected
        {
            protected AbstractProtected() { }
        }

        public class PublicOnly
        {
            public PublicOnly() { }
        }

        public class ImplicitConstructor { }

        public class DerivedFromProtected : AbstractProtected { }

        public class DerivedTwice : DerivedFromProtected { }

        public abstract class AbstractDerived : AbstractProtected { }

        public abstract class AbstractDerivedTwice : AbstractDerived { }

        public class ConcreteBase
        {
            protected ConcreteBase() { }
        }

        public sealed class SealedDerived : ConcreteBase { }

        public class DerivedWithPublic : AbstractProtected
        {
            public DerivedWithPublic() { }
        }

        public class OverloadedPrivate
        {
            private OverloadedPrivate(int x) { }
            private OverloadedPrivate(string x) { }
            public static OverloadedPrivate Make() => new(1);
        }

        public record PrivateRecord
        {
            private PrivateRecord(int x) { X = x; }
            public int X { get; }
            public static PrivateRecord Make() => new(1);
        }

        public struct PrivateStruct
        {
            private PrivateStruct(int x) { X = x; }
            public int X { get; }
            public static PrivateStruct Make() => new(1);
        }

        public static class Helpers
        {
            public static int One() => 1;
        }
        """;

    [Test]
    public async Task SealedGenericWithOnlyAPrivateConstructor_DescribesItselfAsCtor()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert
            .That(result["prop.ts"])
            .Contains("private constructor(value: T)")
            .And.Contains("static get $runtimeType(): ClassType<Prop<unknown>>")
            .And.Contains("ctor: Prop as never,")
            .And.DoesNotContain("as unknown");
    }

    [Test]
    public async Task ClassWithOnlyAProtectedConstructor_DescribesItselfAsCtor()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert
            .That(result["protected-only.ts"])
            .Contains("protected constructor()")
            .And.Contains("static get $runtimeType(): ClassType<ProtectedOnly>")
            .And.Contains("ctor: ProtectedOnly as never,")
            .And.DoesNotContain("as unknown");
    }

    [Test]
    public async Task AbstractClassWithAProtectedConstructor_DescribesItselfAsCtor()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert
            .That(result["abstract-protected.ts"])
            .Contains("protected constructor()")
            .And.Contains("static get $runtimeType(): ClassType<AbstractProtected>")
            .And.Contains("ctor: AbstractProtected as never,")
            .And.Contains("isAbstract: true,")
            .And.DoesNotContain("as unknown");
    }

    [Test]
    public async Task ClassWithAPublicOrImplicitConstructor_KeepsThePlainCtorReference()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert
            .That(result["public-only.ts"])
            .Contains("ctor: PublicOnly,")
            .And.DoesNotContain("as never");

        await Assert
            .That(result["implicit-constructor.ts"])
            .Contains("ctor: ImplicitConstructor,")
            .And.DoesNotContain("as never");
    }

    [Test]
    public async Task DerivedClassOfAProtectedBase_EmitsAPublicConstructorAndKeepsThePlainReference()
    {
        var result = TranspileHelper.Transpile(Source);

        var derived = new Dictionary<string, string>
        {
            ["derived-from-protected.ts"] = "DerivedFromProtected",
            ["derived-twice.ts"] = "DerivedTwice",
            ["abstract-derived.ts"] = "AbstractDerived",
            ["abstract-derived-twice.ts"] = "AbstractDerivedTwice",
            ["sealed-derived.ts"] = "SealedDerived",
        };

        foreach (var (file, name) in derived)
        {
            await Assert
                .That(result[file])
                .Contains("\n  constructor() {")
                .And.Contains($"ctor: {name},\n")
                .And.DoesNotContain("as never");
        }
    }

    [Test]
    public async Task DerivedClassWithAPublicConstructor_KeepsThePlainCtorReference()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert
            .That(result["derived-with-public.ts"])
            .Contains("ctor: DerivedWithPublic,")
            .And.DoesNotContain("as never");
    }

    [Test]
    public async Task OverloadedPrivateConstructors_AssertTheDispatchedClass()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert
            .That(result["overloaded-private.ts"])
            .Contains("private constructor(")
            .And.Contains("ctor: OverloadedPrivate as never,");
    }

    [Test]
    public async Task RecordWithAPrivateConstructor_AssertsTheClass()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert.That(result["private-record.ts"]).Contains("ctor: PrivateRecord as never,");
    }

    [Test]
    public async Task StructWithAPrivateConstructor_KeepsItsPublicConstructorAndPlainReference()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert
            .That(result["private-struct.ts"])
            .Contains("ctor: PrivateStruct,\n")
            .And.DoesNotContain("private constructor")
            .And.DoesNotContain("as never");
    }

    [Test]
    public async Task StaticClass_HasNoClassDescriptor()
    {
        var result = TranspileHelper.Transpile(Source);

        await Assert.That(result).ContainsKey("helpers.ts");
        await Assert.That(result["helpers.ts"]).DoesNotContain("$runtimeType");
    }

    [Test]
    public async Task NonPublicConstructorDescriptors_TypeCheckUnderStrict()
    {
        var output = TranspileHelper.Transpile(Source);

        var (exitCode, log) = await StrictTypeCheck.Run(new Dictionary<string, string>(), output);

        await Assert.That(log).IsEmpty();
        await Assert.That(exitCode).IsEqualTo(0);
    }
}
