namespace Conformance;

// Spec 011 FR-051: a boxed enum hashes as its raw value.
public static class BpHashProbes
{
    public static string BoxedHash()
    {
        object green = BpHue.Green;
        object read = BpAcc.Read;

        return ""
            + (BpHue.Green.GetHashCode() == green.GetHashCode())
            + (BpAcc.Read.GetHashCode() == read.GetHashCode())
            + (green.GetHashCode() == ((object)BpHue.Green).GetHashCode())
            + green.Equals(BpHue.Green)
            + green.Equals(5);
    }
}
