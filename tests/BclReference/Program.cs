using System.Globalization;
using BclReference;

// The runtime formats and parses in the invariant culture.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var fixtures = Path.Combine(
    RepositoryRoot.Find(),
    "targets",
    "js",
    "metano-runtime",
    "test",
    "conformance"
);

var strings = new FixtureWriter();

StringRows.AddTo(strings);
strings.Write(Path.Combine(fixtures, "strings.json"));

var temporal = new FixtureWriter();

TemporalRows.AddTo(temporal);
temporal.Write(Path.Combine(fixtures, "temporal.json"));

var parsing = new FixtureWriter();

ParsingRows.AddTo(parsing);
parsing.Write(Path.Combine(fixtures, "parsing.json"));

var numberParsing = new FixtureWriter();

NumberParsingRows.AddTo(numberParsing);
numberParsing.Write(Path.Combine(fixtures, "number-parsing.json"));
