using Metano.Compiler.IR;

namespace Metano.Tests.IR;

public class IrEnumExtractionTests
{
    [Test]
    public async Task NumericEnum_ExtractsCorrectly()
    {
        var ir = IrTestHelper.ExtractEnum(
            """
            [Transpile]
            public enum Priority
            {
                Low = 0,
                Medium = 1,
                High = 2,
            }
            """
        );

        await Assert.That(ir.Name).IsEqualTo("Priority");
        await Assert.That(ir.Style).IsEqualTo(IrEnumStyle.Numeric);
        await Assert.That(ir.Members).Count().IsEqualTo(3);
        await Assert.That(ir.Members[0].Name).IsEqualTo("Low");
        await Assert.That(ir.Members[0].Value).IsEqualTo(0);
        await Assert.That(ir.Members[1].Name).IsEqualTo("Medium");
        await Assert.That(ir.Members[1].Value).IsEqualTo(1);
        await Assert.That(ir.Members[2].Name).IsEqualTo("High");
        await Assert.That(ir.Members[2].Value).IsEqualTo(2);
    }

    [Test]
    public async Task StringEnum_ExtractsCorrectly()
    {
        var ir = IrTestHelper.ExtractEnum(
            """
            [Transpile]
            [StringEnum]
            public enum Currency
            {
                Dollar,
                Euro,
                [Name("BRL")]
                Real,
            }
            """
        );

        await Assert.That(ir.Name).IsEqualTo("Currency");
        await Assert.That(ir.Style).IsEqualTo(IrEnumStyle.String);
        await Assert.That(ir.Members).Count().IsEqualTo(3);
        await Assert.That(ir.Members[0].Name).IsEqualTo("Dollar");
        await Assert.That(ir.Members[0].Value).IsEqualTo("Dollar");
        await Assert.That(ir.Members[1].Name).IsEqualTo("Euro");
        await Assert.That(ir.Members[1].Value).IsEqualTo("Euro");
        await Assert.That(ir.Members[2].Name).IsEqualTo("Real");
        await Assert.That(ir.Members[2].Value).IsEqualTo("BRL");
    }

    [Test]
    public async Task StringEnum_WithNameOverride_CarriesAttribute()
    {
        var ir = IrTestHelper.ExtractEnum(
            """
            [Transpile]
            [StringEnum]
            public enum Currency
            {
                [Name("BRL")]
                Real,
            }
            """
        );

        await Assert.That(ir.Members[0].Attributes).IsNotNull();
        await Assert.That(ir.Members[0].Attributes!).Count().IsEqualTo(1);
        await Assert.That(ir.Members[0].Attributes![0].Name).IsEqualTo("Name");
    }

    [Test]
    public async Task Enum_HasPublicVisibility()
    {
        var ir = IrTestHelper.ExtractEnum(
            """
            [Transpile]
            public enum Status { Active, Inactive }
            """
        );

        await Assert.That(ir.Visibility).IsEqualTo(IrVisibility.Public);
    }

    [Test]
    public async Task Enum_PreservesOriginalNames()
    {
        var ir = IrTestHelper.ExtractEnum(
            """
            [Transpile]
            public enum HttpMethod { GET, POST, PUT, DELETE }
            """
        );

        await Assert.That(ir.Members[0].Name).IsEqualTo("GET");
        await Assert.That(ir.Members[1].Name).IsEqualTo("POST");
    }
}
