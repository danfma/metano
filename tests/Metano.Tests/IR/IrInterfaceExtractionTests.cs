using Metano.Compiler.IR;

namespace Metano.Tests.IR;

public class IrInterfaceExtractionTests
{
    [Test]
    public async Task SimpleInterface_ExtractsProperties()
    {
        var ir = IrTestHelper.ExtractInterface(
            """
            [Transpile]
            public interface ITodoItem
            {
                string Title { get; }
                bool IsCompleted { get; set; }
            }
            """
        );

        await Assert.That(ir.Name).IsEqualTo("ITodoItem");
        await Assert.That(ir.Members).Count().IsEqualTo(2);

        var title = ir.Members![0] as IrPropertyDeclaration;
        await Assert.That(title).IsNotNull();
        await Assert.That(title!.Name).IsEqualTo("Title");
        await Assert.That(title.Accessors).IsEqualTo(IrPropertyAccessors.GetOnly);
        await Assert.That(title.Type).IsTypeOf<IrPrimitiveTypeRef>();

        var completed = ir.Members[1] as IrPropertyDeclaration;
        await Assert.That(completed).IsNotNull();
        await Assert.That(completed!.Name).IsEqualTo("IsCompleted");
        await Assert.That(completed.Accessors).IsEqualTo(IrPropertyAccessors.GetSet);
    }

    [Test]
    public async Task Interface_WithMethods_ExtractsMethods()
    {
        var ir = IrTestHelper.ExtractInterface(
            """
            [Transpile]
            public interface IRepository
            {
                Task<bool> SaveAsync(string name, int value);
            }
            """
        );

        await Assert.That(ir.Members).Count().IsEqualTo(1);
        var method = ir.Members![0] as IrMethodDeclaration;
        await Assert.That(method).IsNotNull();
        await Assert.That(method!.Name).IsEqualTo("SaveAsync");
        await Assert.That(method.Parameters).Count().IsEqualTo(2);
        await Assert.That(method.Parameters[0].Name).IsEqualTo("name");
        await Assert.That(method.Parameters[1].Name).IsEqualTo("value");
        // Return type should be Promise<Boolean>
        await Assert.That(method.ReturnType).IsTypeOf<IrPromiseTypeRef>();
    }

    [Test]
    public async Task Interface_WithGenericTypeParam_ExtractsTypeParameters()
    {
        var ir = IrTestHelper.ExtractInterface(
            """
            [Transpile]
            public interface IResult<T>
            {
                T Value { get; }
                bool Success { get; }
            }
            """
        );

        await Assert.That(ir.TypeParameters).Count().IsEqualTo(1);
        await Assert.That(ir.TypeParameters![0].Name).IsEqualTo("T");
    }

    [Test]
    public async Task Interface_IgnoredMembers_AreSkipped()
    {
        var ir = IrTestHelper.ExtractInterface(
            """
            [Transpile]
            public interface IItem
            {
                string Name { get; }
                [Ignore]
                string InternalId { get; }
            }
            """
        );

        await Assert.That(ir.Members).Count().IsEqualTo(1);
        await Assert.That((ir.Members![0] as IrPropertyDeclaration)!.Name).IsEqualTo("Name");
    }

    [Test]
    public async Task Interface_NamesPreservedInPascalCase()
    {
        var ir = IrTestHelper.ExtractInterface(
            """
            [Transpile]
            public interface IFoo
            {
                string FirstName { get; }
            }
            """
        );

        // IR preserves PascalCase — camelCase is a target concern
        var prop = ir.Members![0] as IrPropertyDeclaration;
        await Assert.That(prop!.Name).IsEqualTo("FirstName");
    }

    [Test]
    public async Task Interface_WithEvent_ExtractsEvent()
    {
        var ir = IrTestHelper.ExtractInterface(
            """
            [Transpile]
            public interface IObservable
            {
                event EventHandler Changed;
            }
            """
        );

        await Assert.That(ir.Members).Count().IsEqualTo(1);
        var evt = ir.Members![0] as IrEventDeclaration;
        await Assert.That(evt).IsNotNull();
        await Assert.That(evt!.Name).IsEqualTo("Changed");
    }

    [Test]
    public async Task Interface_WithNameOverride_PreservesAttribute()
    {
        var ir = IrTestHelper.ExtractInterface(
            """
            [Transpile]
            public interface IDto
            {
                [Name("user_id")]
                string UserId { get; }
            }
            """
        );

        var prop = ir.Members![0] as IrPropertyDeclaration;
        await Assert.That(prop).IsNotNull();
        await Assert.That(prop!.Name).IsEqualTo("UserId");
        await Assert.That(prop.Attributes).IsNotNull();
        await Assert.That(prop.Attributes!).Count().IsEqualTo(1);
        await Assert.That(prop.Attributes![0].Name).IsEqualTo("Name");
    }

    [Test]
    public async Task Interface_WithDefaultImplementation_FlagsIt()
    {
        var ir = IrTestHelper.ExtractInterface(
            """
            [Transpile]
            public interface IGreeter
            {
                string Greet(string name) => $"Hello, {name}!";
            }
            """
        );

        var method = ir.Members![0] as IrMethodDeclaration;
        await Assert.That(method).IsNotNull();
        await Assert.That(method!.Semantics.HasDefaultImplementation).IsTrue();
    }
}
