namespace SampleJsonContracts;

// A [Flags] enum: System.Text.Json reads a combination (`3` = Read | Write)
// and any other integer of the underlying type (#369).
[Flags]
public enum Perms
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
}
