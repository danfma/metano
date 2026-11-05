using System.Globalization;
using Int64Reference;
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
    typeof(DecimalConversions)
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
