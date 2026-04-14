namespace SampleOperatorOverloading;

public sealed class NoSameMoneyCurrencyException(Currency expected, Currency provided)
    : Exception($"Not same currency. Money has {provided}, but expected {expected}");
