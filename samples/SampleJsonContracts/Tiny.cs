namespace SampleJsonContracts;

// A byte-backed enum: System.Text.Json range-checks a read by the underlying
// type, so `300` throws while `200` reads although no member declares it (#369).
public enum Tiny : byte
{
    Small = 1,
    Big = 2,
}
