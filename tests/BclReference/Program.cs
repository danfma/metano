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
