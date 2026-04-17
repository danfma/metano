using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Tests.IR;

public class IrClassExtractionTests
{
    [Test]
    public async Task SimpleClass_ExtractsHeader()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public sealed class User
            {
                public readonly int _age = 0;
            }
            """
        );

        await Assert.That(ir.Name).IsEqualTo("User");
        await Assert.That(ir.Visibility).IsEqualTo(IrVisibility.Public);
        await Assert.That(ir.Semantics.IsSealed).IsTrue();
        await Assert.That(ir.Semantics.IsRecord).IsFalse();
        await Assert.That(ir.BaseType).IsNull();
    }

    [Test]
    public async Task Record_HasRecordSemantics()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public record User(string Name, int Age);
            """
        );

        await Assert.That(ir.Semantics.IsRecord).IsTrue();
    }

    [Test]
    public async Task Struct_HasValueTypeSemantics()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public readonly struct Money
            {
                public readonly decimal Value;
                public Money(decimal value) { Value = value; }
            }
            """
        );

        await Assert.That(ir.Semantics.IsValueType).IsTrue();
    }

    [Test]
    public async Task StaticClass_HasStaticSemantics()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public static class Utils
            {
                public static int Answer = 42;
            }
            """
        );

        await Assert.That(ir.Semantics.IsStatic).IsTrue();
    }

    [Test]
    public async Task PlainObjectClass_HasPlainObjectSemantics()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            [PlainObject]
            public record UserDto(string Name, int Age);
            """
        );

        await Assert.That(ir.Semantics.IsPlainObject).IsTrue();
        await Assert.That(ir.Semantics.IsRecord).IsTrue();
    }

    [Test]
    public async Task ClassWithTranspilableBase_RecordsBaseType()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Base { }

            [Transpile]
            public class Derived : Base { }
            """,
            typeName: "Derived"
        );

        await Assert.That(ir.BaseType).IsNotNull();
        var baseRef = ir.BaseType as IrNamedTypeRef;
        await Assert.That(baseRef).IsNotNull();
        await Assert.That(baseRef!.Name).IsEqualTo("Base");
    }

    [Test]
    public async Task ClassInheritingFromObject_HasNullBase()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Standalone { }
            """
        );

        // System.Object should not be surfaced as a meaningful base
        await Assert.That(ir.BaseType).IsNull();
    }

    [Test]
    public async Task ClassImplementingInterfaces_RecordsInterfaces()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Worker : IDisposable
            {
                public void Dispose() { }
            }
            """
        );

        await Assert.That(ir.Interfaces).IsNotNull();
        await Assert.That(ir.Interfaces!).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task GenericClass_ExtractsTypeParameters()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Box<T, TKey>
            {
                public readonly T Value = default!;
            }
            """
        );

        await Assert.That(ir.TypeParameters).IsNotNull();
        await Assert.That(ir.TypeParameters!).Count().IsEqualTo(2);
        await Assert.That(ir.TypeParameters![0].Name).IsEqualTo("T");
        await Assert.That(ir.TypeParameters[1].Name).IsEqualTo("TKey");
    }

    [Test]
    public async Task ClassWithNameOverride_CarriesAttribute()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            [Name("Customer")]
            public class User { }
            """
        );

        await Assert.That(ir.Attributes).IsNotNull();
        await Assert.That(ir.Attributes!).Count().IsEqualTo(1);
        await Assert.That(ir.Attributes![0].Name).IsEqualTo("Name");
    }

    [Test]
    public async Task ClassWithFields_ExtractsFields()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Counter
            {
                public int Value;
                public readonly string Name = "";
                private bool _internal;
            }
            """
        );

        await Assert.That(ir.Members).IsNotNull();
        await Assert.That(ir.Members!).Count().IsEqualTo(3);

        var value = ir.Members![0] as IrFieldDeclaration;
        await Assert.That(value!.Name).IsEqualTo("Value");
        await Assert.That(value.IsReadonly).IsFalse();

        var name = ir.Members[1] as IrFieldDeclaration;
        await Assert.That(name!.Name).IsEqualTo("Name");
        await Assert.That(name.IsReadonly).IsTrue();

        var internalField = ir.Members[2] as IrFieldDeclaration;
        await Assert.That(internalField!.Visibility).IsEqualTo(IrVisibility.Private);
    }

    [Test]
    public async Task AutoPropertyBackingField_IsSkipped()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class User
            {
                public string Name { get; set; } = "";
            }
            """
        );

        // The backing field for Name is compiler-generated; it should not be
        // emitted as an IrFieldDeclaration. Properties are not yet extracted
        // (Phase 5.2+), so Members should be null or empty.
        if (ir.Members is not null)
        {
            foreach (var m in ir.Members)
            {
                await Assert.That(m).IsNotTypeOf<IrFieldDeclaration>();
            }
        }
    }

    [Test]
    public async Task AutoProperty_ExtractedWithAccessorShape()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class User
            {
                public string Name { get; set; } = "";
                public int Age { get; }
                public bool IsActive { get; init; }
            }
            """
        );

        await Assert.That(ir.Members).IsNotNull();
        var props = ir.Members!.OfType<IrPropertyDeclaration>().ToList();
        await Assert.That(props).Count().IsEqualTo(3);

        var name = props[0];
        await Assert.That(name.Name).IsEqualTo("Name");
        await Assert.That(name.Accessors).IsEqualTo(IrPropertyAccessors.GetSet);
        await Assert.That(name.Semantics!.HasInitializer).IsTrue();
        await Assert.That(name.Semantics.HasGetterBody).IsFalse();

        var age = props[1];
        await Assert.That(age.Accessors).IsEqualTo(IrPropertyAccessors.GetOnly);

        var isActive = props[2];
        await Assert.That(isActive.Accessors).IsEqualTo(IrPropertyAccessors.GetInit);
    }

    [Test]
    public async Task PropertyWithPrivateSetter_PreservesSetterVisibility()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Workflow
            {
                public string Status { get; private set; } = "pending";
            }
            """
        );

        var prop = ir.Members!.OfType<IrPropertyDeclaration>().First();
        await Assert.That(prop.Visibility).IsEqualTo(IrVisibility.Public);
        await Assert.That(prop.SetterVisibility).IsEqualTo(IrVisibility.Private);
    }

    [Test]
    public async Task ComputedProperty_SignalsGetterBody()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Person
            {
                public string First { get; init; } = "";
                public string Last { get; init; } = "";
                public string FullName => $"{First} {Last}";
            }
            """
        );

        var fullName = ir.Members!.OfType<IrPropertyDeclaration>().First(p => p.Name == "FullName");
        await Assert.That(fullName.Semantics!.HasGetterBody).IsTrue();
        await Assert.That(fullName.Semantics.HasInitializer).IsFalse();
        await Assert.That(fullName.Accessors).IsEqualTo(IrPropertyAccessors.GetOnly);
    }

    [Test]
    public async Task BlockBodiedSetter_SignalsSetterBody()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Account
            {
                private string _email = "";
                public string Email
                {
                    get => _email;
                    set { _email = value.ToLower(); }
                }
            }
            """
        );

        var email = ir.Members!.OfType<IrPropertyDeclaration>().First(p => p.Name == "Email");
        await Assert.That(email.Semantics!.HasGetterBody).IsTrue();
        await Assert.That(email.Semantics.HasSetterBody).IsTrue();
    }

    [Test]
    public async Task VirtualProperty_FlagsVirtual()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Shape
            {
                public virtual double Area { get; } = 0;
            }
            """
        );

        var area = ir.Members!.OfType<IrPropertyDeclaration>().First();
        await Assert.That(area.Semantics!.IsVirtual).IsTrue();
    }

    [Test]
    public async Task StaticProperty_FlagsStatic()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Config
            {
                public static string Environment { get; set; } = "dev";
            }
            """
        );

        var env = ir.Members!.OfType<IrPropertyDeclaration>().First();
        await Assert.That(env.IsStatic).IsTrue();
    }

    [Test]
    public async Task IgnoredProperty_IsSkipped()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Model
            {
                public string Visible { get; set; } = "";

                [Ignore]
                public string Hidden { get; set; } = "";
            }
            """
        );

        var props = ir.Members!.OfType<IrPropertyDeclaration>().ToList();
        await Assert.That(props).Count().IsEqualTo(1);
        await Assert.That(props[0].Name).IsEqualTo("Visible");
    }

    [Test]
    public async Task PropertyWithNameOverride_CarriesAttribute()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Dto
            {
                [Name("user_id")]
                public string UserId { get; set; } = "";
            }
            """
        );

        var prop = ir.Members!.OfType<IrPropertyDeclaration>().First();
        await Assert.That(prop.Attributes).IsNotNull();
        await Assert.That(prop.Attributes![0].Name).IsEqualTo("Name");
    }

    [Test]
    public async Task Methods_ExtractedWithSignatures()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Service
            {
                public string Greet(string name) => $"Hi {name}";
                public Task<int> CountAsync() => Task.FromResult(0);
                public static void LogError(string msg) { }
            }
            """
        );

        var methods = ir.Members!.OfType<IrMethodDeclaration>().ToList();
        await Assert.That(methods).Count().IsEqualTo(3);

        var greet = methods[0];
        await Assert.That(greet.Name).IsEqualTo("Greet");
        await Assert.That(greet.Parameters).Count().IsEqualTo(1);
        await Assert.That(greet.Parameters[0].Name).IsEqualTo("name");
        await Assert.That(greet.IsStatic).IsFalse();

        var countAsync = methods[1];
        await Assert.That(countAsync.Semantics.IsAsync).IsFalse(); // no async keyword
        await Assert.That(countAsync.ReturnType).IsTypeOf<IrPromiseTypeRef>();

        var logError = methods[2];
        await Assert.That(logError.IsStatic).IsTrue();
    }

    [Test]
    public async Task AsyncMethod_FlagsAsync()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Worker
            {
                public async Task DoAsync() => await Task.Delay(1);
            }
            """
        );

        var m = ir.Members!.OfType<IrMethodDeclaration>().First();
        await Assert.That(m.Semantics.IsAsync).IsTrue();
    }

    [Test]
    public async Task IteratorMethod_FlagsGeneratorAndMapsReturnType()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Range
            {
                public IEnumerable<int> Numbers()
                {
                    yield return 1;
                    yield return 2;
                }
            }
            """
        );

        var m = ir.Members!.OfType<IrMethodDeclaration>().First();
        await Assert.That(m.Semantics.IsGenerator).IsTrue();
        await Assert.That(m.ReturnType).IsTypeOf<IrGeneratorTypeRef>();
    }

    [Test]
    public async Task AbstractMethod_FlagsAbstract()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public abstract class Shape
            {
                public abstract double Area();
            }
            """
        );

        var m = ir.Members!.OfType<IrMethodDeclaration>().First();
        await Assert.That(m.Semantics.IsAbstract).IsTrue();
    }

    [Test]
    public async Task VirtualMethod_FlagsVirtual()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Animal
            {
                public virtual string Sound() => "generic";
            }
            """
        );

        var m = ir.Members!.OfType<IrMethodDeclaration>().First();
        await Assert.That(m.Semantics.IsVirtual).IsTrue();
    }

    [Test]
    public async Task OperatorOverload_FlagsOperatorKind()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public readonly struct Money
            {
                public readonly decimal Value;
                public Money(decimal v) { Value = v; }
                public static Money operator +(Money a, Money b) => new(a.Value + b.Value);
            }
            """
        );

        var opMethod = ir.Members!.OfType<IrMethodDeclaration>().First(m => m.Semantics.IsOperator);
        await Assert.That(opMethod.Semantics.IsOperator).IsTrue();
        await Assert.That(opMethod.Semantics.OperatorKind).IsEqualTo("Addition");
    }

    [Test]
    public async Task MethodWithTypeParameters_ExtractsThem()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Utils
            {
                public T Identity<T>(T value) => value;
            }
            """
        );

        var m = ir.Members!.OfType<IrMethodDeclaration>().First();
        await Assert.That(m.TypeParameters).IsNotNull();
        await Assert.That(m.TypeParameters!).Count().IsEqualTo(1);
        await Assert.That(m.TypeParameters![0].Name).IsEqualTo("T");
    }

    [Test]
    public async Task Events_ExtractedAsEventDeclarations()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Observable
            {
                public event EventHandler? Changed;
            }
            """
        );

        var evt = ir.Members!.OfType<IrEventDeclaration>().First();
        await Assert.That(evt.Name).IsEqualTo("Changed");
    }

    [Test]
    public async Task RecordPrimaryConstructor_ExtractsWithReadonlyPromotion()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public record User(string Name, int Age);
            """
        );

        await Assert.That(ir.Constructor).IsNotNull();
        await Assert.That(ir.Constructor!.Parameters).Count().IsEqualTo(2);

        var nameParam = ir.Constructor.Parameters[0];
        await Assert.That(nameParam.Parameter.Name).IsEqualTo("Name");
        await Assert.That(nameParam.Promotion).IsEqualTo(IrParameterPromotion.ReadonlyProperty);

        var ageParam = ir.Constructor.Parameters[1];
        await Assert.That(ageParam.Parameter.Name).IsEqualTo("Age");
        await Assert.That(ageParam.Promotion).IsEqualTo(IrParameterPromotion.ReadonlyProperty);
    }

    [Test]
    public async Task ExplicitConstructor_WithoutPromotion_FlagsParamsAsNone()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Service
            {
                public Service(string connectionString) { }
            }
            """
        );

        var ctor = ir.Constructor!;
        await Assert.That(ctor.Parameters).Count().IsEqualTo(1);
        await Assert.That(ctor.Parameters[0].Promotion).IsEqualTo(IrParameterPromotion.None);
    }

    [Test]
    public async Task ConstructorWithDefaultValue_FlagsHasDefault()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public record Pagination(int Page, int Size = 20);
            """
        );

        var ctor = ir.Constructor!;
        await Assert.That(ctor.Parameters[0].Parameter.HasDefaultValue).IsFalse();
        await Assert.That(ctor.Parameters[1].Parameter.HasDefaultValue).IsTrue();
    }

    [Test]
    public async Task MultipleConstructors_RecordedAsOverloads()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Point
            {
                public double X { get; }
                public double Y { get; }
                public Point(double x, double y) { X = x; Y = y; }
                public Point(double v) : this(v, v) { }
            }
            """
        );

        var ctor = ir.Constructor!;
        // Primary is the 2-param version (most params)
        await Assert.That(ctor.Parameters).Count().IsEqualTo(2);
        await Assert.That(ctor.Overloads).IsNotNull();
        await Assert.That(ctor.Overloads!).Count().IsEqualTo(1);
        await Assert.That(ctor.Overloads![0].Parameters).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ClassWithoutExplicitConstructor_HasNullConstructor()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class Empty { }
            """
        );

        await Assert.That(ir.Constructor).IsNull();
    }

    [Test]
    public async Task MutableProperty_PromotedAsMutable()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class State
            {
                public string Name { get; set; }
                public State(string name) { Name = name; }
            }
            """
        );

        var ctor = ir.Constructor!;
        await Assert
            .That(ctor.Parameters[0].Promotion)
            .IsEqualTo(IrParameterPromotion.MutableProperty);
    }

    [Test]
    public async Task ExceptionClass_HasExceptionSemantics()
    {
        var ir = ExtractClass(
            """
            [Transpile]
            public class AppError : Exception
            {
                public AppError(string message) : base(message) { }
            }
            """
        );

        await Assert.That(ir.Semantics.IsException).IsTrue();
    }

    // -- helpers --

    private static IrClassDeclaration ExtractClass(string csharpSource, string? typeName = null)
    {
        var compilation = IrTestHelper.Compile(csharpSource);
        INamedTypeSymbol? found = null;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (model.GetDeclaredSymbol(node) is not INamedTypeSymbol named)
                    continue;
                if (
                    named.TypeKind is not (TypeKind.Class or TypeKind.Struct)
                    || !Metano.Compiler.SymbolHelper.HasTranspile(named)
                )
                    continue;
                if (typeName is null || named.Name == typeName)
                {
                    found = named;
                    break;
                }
            }
            if (found is not null)
                break;
        }

        if (found is null)
            throw new InvalidOperationException(
                $"No [Transpile]-annotated class/struct{(typeName is null ? "" : $" named {typeName}")} found."
            );

        return IrClassExtractor.Extract(found);
    }
}
