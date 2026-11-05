namespace SampleJsonContracts;

// The holders behind the #471 rows: non-nullable value-type members whose
// JSON `null` throws System.Text.Json's conversion error — a property names
// the declaring contract, an element or a dictionary value names the element
// type, and a struct property names the struct itself.
public record HolderInt(int N);

public record HolderLevel(Level Level);

public record HolderChar(char C);

public record HolderSPoint(SPoint S);
