using System.Globalization;
using Int64Reference;
using SampleCheckedConformance;
using SampleInt64Conformance;

// The twin formats and parses in the invariant culture, as the runtime does.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var fixtures = Path.Combine(RepositoryRoot.Find(), "tests", "Int64Reference", "fixtures");

ReferenceFixture.Write(
    Path.Combine(fixtures, "operators.json"),
    typeof(Overflow),
    typeof(Division),
    typeof(Shifts)
);

ReferenceFixture.Write(
    Path.Combine(fixtures, "conversions.json"),
    typeof(Conversions),
    typeof(DecimalConversions),
    typeof(BigIntegerConversions),
    typeof(DecimalBigIntegerConversions)
);

ReferenceFixture.Write(Path.Combine(fixtures, "math.json"), typeof(MathStatics));

ReferenceFixture.Write(
    Path.Combine(fixtures, "formatting.json"),
    typeof(Formatting),
    typeof(Parsing),
    typeof(ConvertCalls),
    typeof(DecimalConvertCalls)
);

ReferenceFixture.Write(Path.Combine(fixtures, "aggregates.json"), typeof(Aggregates));

ReferenceFixture.Write(Path.Combine(fixtures, "comparisons.json"), typeof(Comparisons));

ReferenceFixture.Write(Path.Combine(fixtures, "indexing.json"), typeof(Indexing));
ReferenceFixture.Write(Path.Combine(fixtures, "tuples.json"), typeof(TupleConversions));
ReferenceFixture.Write(Path.Combine(fixtures, "nullable-unwraps.json"), typeof(NullableUnwraps));

ReferenceFixture.Write(
    Path.Combine(fixtures, "narrow-integers.json"),
    typeof(NarrowConversions),
    typeof(NarrowCompound),
    typeof(UnsignedBits),
    typeof(CheckedArithmetic),
    typeof(NarrowShapes)
);

ReferenceFixture.Write(Path.Combine(fixtures, "project-checked.json"), typeof(ProjectChecked));
