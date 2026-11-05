namespace SampleJsonContracts;

// The abstract registration of FR-017: its tag reads as System.Text.Json's
// abstract-type NotSupportedException (manifest row E25, DECIDED-12), while
// an unregistered concrete subtype of it is written through it (W12 uses the
// concrete UNearA for that).
public abstract record UNearAbs(int A) : UNear(A);
