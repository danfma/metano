namespace SampleOperatorOverloading;

/// <summary>
/// A currency code that does not name a supported <see cref="Currency"/>.
/// Exercises the BCL <see cref="ArgumentException"/> message composition
/// (<c>" (Parameter 'code')"</c>) on the transpiled side.
/// </summary>
public class InvalidCurrencyCodeException : ArgumentException
{
    public InvalidCurrencyCodeException(string code)
        : base($"Unknown currency code '{code}'", "code") { }
}

/// <summary>
/// A known code the sample does not trade. Overrides <see cref="Message"/> two
/// levels below the BCL base, reading the inherited message through
/// <c>base.Message</c>.
/// </summary>
public sealed class UnsupportedCurrencyException : InvalidCurrencyCodeException
{
    public UnsupportedCurrencyException(string code)
        : base(code) { }

    public override string Message => $"Unsupported currency. {base.Message}";
}

/// <summary>
/// A failed conversion between two currencies, chaining its constructors
/// through <c>: this(...)</c> and keeping the underlying failure as the inner
/// exception.
/// </summary>
public sealed class CurrencyConversionException : Exception
{
    public CurrencyConversionException(string fromCode, string toCode)
        : this(fromCode, toCode, new InvalidOperationException("No exchange rate available")) { }

    public CurrencyConversionException(string fromCode, string toCode, Exception reason)
        : base($"Cannot convert {fromCode} to {toCode}", reason) { }

    public override string Message => $"Conversion failed: {base.Message}";
}
