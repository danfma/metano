namespace SampleIssueTracker.Planning.Domain;

/// <summary>
/// A story-point estimate: the overload-factories scenario of spec 013 (FR-021,
/// SC-006). Its constructor overloads become factories that generated code
/// constructs through (<c>Estimate.fromPoints(0)</c>), its virtual
/// <c>Add</c> group becomes addressable fast paths that generated calls bind
/// directly, and <see cref="None"/> is initialized through a factory before
/// the constructors are declared (R9). Hand-written TypeScript keeps
/// <c>new Estimate(3)</c> and <c>estimate.add(2)</c>: the runtime dispatchers
/// stay as the fallback.
/// </summary>
public class Estimate
{
    public static readonly Estimate None = new Estimate(0);

    public int Points { get; }

    public string Note { get; }

    public Estimate(int points)
        : this(points, "") { }

    public Estimate(int points, string note)
    {
        Points = points;
        Note = note;
    }

    public Estimate(Estimate first, Estimate second)
        : this(first.Points + second.Points, "combined") { }

    public virtual Estimate Add(int points) => new Estimate(Points + points, Note);

    public virtual Estimate Add(Estimate other) => new Estimate(this, other);

    public override string ToString() =>
        Note.Length == 0 ? $"{Points} pt" : $"{Points} pt ({Note})";
}

/// <summary>
/// An estimate padded by a fixed buffer: it overrides one overload of the
/// inherited <c>Add</c> group (the fast path <c>addPoints</c>, emitted
/// <c>override</c>) and calls the base overload through <c>super.addPoints</c>.
/// </summary>
public class BufferedEstimate : Estimate
{
    public int Buffer { get; }

    public BufferedEstimate(int points, int buffer)
        : base(points) => Buffer = buffer;

    public override Estimate Add(int points) => base.Add(points + Buffer);
}

/// <summary>
/// An extension group (<c>Scale</c>) lowered to exported module fast paths beside its
/// dispatcher, and a module function whose calls bind them.
/// </summary>
public static class EstimateMath
{
    public static Estimate Scale(this Estimate estimate, int factor) =>
        new Estimate(estimate.Points * factor, estimate.Note);

    public static Estimate Scale(this Estimate estimate, string note) =>
        new Estimate(estimate.Points, note);

    public static Estimate Total(IReadOnlyList<Estimate> estimates)
    {
        var total = Estimate.None;

        foreach (var estimate in estimates)
            total = total.Add(estimate);

        return total;
    }

    public static Estimate Plan(Estimate estimate, int sprints) =>
        estimate.Scale(sprints).Scale("planned");
}
