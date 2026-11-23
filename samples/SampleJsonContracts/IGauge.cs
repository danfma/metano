namespace SampleJsonContracts;

// A non-polymorphic interface a member is typed as (#502): System.Text.Json
// writes it through its declared properties and cannot read an object into it.
public interface IGauge
{
    int Level { get; }
}
