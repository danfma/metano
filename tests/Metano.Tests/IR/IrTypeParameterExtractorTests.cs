using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Tests.IR;

public class IrTypeParameterExtractorTests
{
    [Test]
    [Arguments("", IrTypeKindConstraint.Any)]
    [Arguments("where T : class", IrTypeKindConstraint.ReferenceType)]
    [Arguments("where T : class?", IrTypeKindConstraint.ReferenceType)]
    [Arguments("where T : struct", IrTypeKindConstraint.ValueType)]
    [Arguments("where T : unmanaged", IrTypeKindConstraint.ValueType)]
    [Arguments("where T : notnull", IrTypeKindConstraint.Any)]
    public async Task KindConstraint_ReflectsTheClassOrStructConstraint(
        string constraint,
        IrTypeKindConstraint expected
    )
    {
        var compilation = IrTestHelper.Compile($"public class Box<T> {constraint} {{ }}");
        var box = compilation.GetTypeByMetadataName("Box`1")!;

        var result = IrTypeParameterExtractor.Extract(box.TypeParameters[0], originResolver: null);

        await Assert.That(result.KindConstraint).IsEqualTo(expected);
    }

    [Test]
    public async Task Constraints_KeepTheConstraintTypesAlongsideTheKind()
    {
        var compilation = IrTestHelper.Compile(
            """
            public interface IShape { }
            public class Box<T> where T : class, IShape { }
            """
        );
        var box = compilation.GetTypeByMetadataName("Box`1")!;

        var result = IrTypeParameterExtractor.Extract(box.TypeParameters[0], originResolver: null);

        await Assert.That(result.KindConstraint).IsEqualTo(IrTypeKindConstraint.ReferenceType);
        await Assert.That(result.Constraints!.Count).IsEqualTo(1);
        await Assert.That(result.Constraints[0]).IsTypeOf<IrNamedTypeRef>();
    }

    [Test]
    public async Task ExtractAll_ReturnsNullWithoutTypeParameters()
    {
        var compilation = IrTestHelper.Compile("public class Plain { }");
        var plain = compilation.GetTypeByMetadataName("Plain")!;

        await Assert
            .That(IrTypeParameterExtractor.ExtractAll(plain.TypeParameters, originResolver: null))
            .IsNull();
    }
}
