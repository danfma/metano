using Int64Reference;
using SampleInt64Conformance;

var fixtures = Path.Combine(RepositoryRoot.Find(), "tests", "Int64Reference", "fixtures");

ReferenceFixture.Write(Path.Combine(fixtures, "operators.json"), typeof(Overflow));

ReferenceFixture.Write(
    Path.Combine(fixtures, "conversions.json"),
    typeof(Conversions),
    typeof(DecimalConversions)
);
