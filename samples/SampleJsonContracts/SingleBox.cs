namespace SampleJsonContracts;

// A `float` member and a nullable one (#695): System.Text.Json reads a JSON
// number into a `Single` rounded to single precision and writes the shortest
// text that reads back as the same `float` (`0.1`, `1E+20`).
public sealed record SingleBox(float F, float? G);
