using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;

namespace Metano.Tests.IR;

public class IrTypeRefMapperTests
{
    [Test]
    public async Task Int32_MapsToPrimitive()
    {
        var compilation = IrTestHelper.Compile("public class X { }");
        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
        var result = IrTypeRefMapper.Map(intType);

        await Assert.That(result).IsTypeOf<IrPrimitiveTypeRef>();
        await Assert.That(((IrPrimitiveTypeRef)result).Primitive).IsEqualTo(IrPrimitive.Int32);
    }

    [Test]
    public async Task String_MapsToPrimitive()
    {
        var compilation = IrTestHelper.Compile("public class X { }");
        var stringType = compilation.GetSpecialType(SpecialType.System_String);
        var result = IrTypeRefMapper.Map(stringType);

        await Assert.That(result).IsTypeOf<IrPrimitiveTypeRef>();
        await Assert.That(((IrPrimitiveTypeRef)result).Primitive).IsEqualTo(IrPrimitive.String);
    }

    [Test]
    public async Task NullableInt_MapsToNullable()
    {
        var compilation = IrTestHelper.Compile("public class X { int? Value { get; } }");
        var xType = compilation.GetTypeByMetadataName("X")!;
        var prop = xType.GetMembers("Value").OfType<IPropertySymbol>().First();
        var result = IrTypeRefMapper.Map(prop.Type);

        await Assert.That(result).IsTypeOf<IrNullableTypeRef>();
        var inner = ((IrNullableTypeRef)result).Inner;
        await Assert.That(inner).IsTypeOf<IrPrimitiveTypeRef>();
        await Assert.That(((IrPrimitiveTypeRef)inner).Primitive).IsEqualTo(IrPrimitive.Int32);
    }

    [Test]
    public async Task ListOfString_MapsToArray()
    {
        var compilation = IrTestHelper.Compile(
            """
            public class X { List<string> Items { get; } = new(); }
            """
        );
        var xType = compilation.GetTypeByMetadataName("X")!;
        var prop = xType.GetMembers("Items").OfType<IPropertySymbol>().First();
        var result = IrTypeRefMapper.Map(prop.Type);

        await Assert.That(result).IsTypeOf<IrArrayTypeRef>();
        var element = ((IrArrayTypeRef)result).ElementType;
        await Assert.That(element).IsTypeOf<IrPrimitiveTypeRef>();
        await Assert.That(((IrPrimitiveTypeRef)element).Primitive).IsEqualTo(IrPrimitive.String);
    }

    [Test]
    public async Task Dictionary_MapsToMap()
    {
        var compilation = IrTestHelper.Compile(
            """
            public class X { Dictionary<string, int> Map { get; } = new(); }
            """
        );
        var xType = compilation.GetTypeByMetadataName("X")!;
        var prop = xType.GetMembers("Map").OfType<IPropertySymbol>().First();
        var result = IrTypeRefMapper.Map(prop.Type);

        await Assert.That(result).IsTypeOf<IrMapTypeRef>();
    }

    [Test]
    public async Task TaskOfBool_MapsToPromise()
    {
        var compilation = IrTestHelper.Compile(
            """
            public class X { Task<bool> DoAsync() => Task.FromResult(true); }
            """
        );
        var xType = compilation.GetTypeByMetadataName("X")!;
        var method = xType.GetMembers("DoAsync").OfType<IMethodSymbol>().First();
        var result = IrTypeRefMapper.Map(method.ReturnType);

        await Assert.That(result).IsTypeOf<IrPromiseTypeRef>();
        var inner = ((IrPromiseTypeRef)result).ResultType;
        await Assert.That(inner).IsTypeOf<IrPrimitiveTypeRef>();
        await Assert.That(((IrPrimitiveTypeRef)inner).Primitive).IsEqualTo(IrPrimitive.Boolean);
    }

    [Test]
    public async Task Guid_MapsToPrimitive()
    {
        var compilation = IrTestHelper.Compile(
            """
            public class X { Guid Id { get; } }
            """
        );
        var xType = compilation.GetTypeByMetadataName("X")!;
        var prop = xType.GetMembers("Id").OfType<IPropertySymbol>().First();
        var result = IrTypeRefMapper.Map(prop.Type);

        await Assert.That(result).IsTypeOf<IrPrimitiveTypeRef>();
        await Assert.That(((IrPrimitiveTypeRef)result).Primitive).IsEqualTo(IrPrimitive.Guid);
    }

    [Test]
    public async Task DateTime_MapsToPrimitive()
    {
        var compilation = IrTestHelper.Compile(
            """
            public class X { DateTime Created { get; } }
            """
        );
        var xType = compilation.GetTypeByMetadataName("X")!;
        var prop = xType.GetMembers("Created").OfType<IPropertySymbol>().First();
        var result = IrTypeRefMapper.Map(prop.Type);

        await Assert.That(result).IsTypeOf<IrPrimitiveTypeRef>();
        await Assert.That(((IrPrimitiveTypeRef)result).Primitive).IsEqualTo(IrPrimitive.DateTime);
    }

    [Test]
    public async Task TypeParameter_MapsToTypeParameterRef()
    {
        var compilation = IrTestHelper.Compile(
            """
            public class Box<T> { T Value { get; set; } = default!; }
            """
        );
        var boxType = compilation.GetTypeByMetadataName("Box`1")!;
        var prop = boxType.GetMembers("Value").OfType<IPropertySymbol>().First();
        var result = IrTypeRefMapper.Map(prop.Type);

        await Assert.That(result).IsTypeOf<IrTypeParameterRef>();
        await Assert.That(((IrTypeParameterRef)result).Name).IsEqualTo("T");
    }
}
