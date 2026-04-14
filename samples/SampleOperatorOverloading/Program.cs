using Metano.Annotations;
using SampleOperatorOverloading;

[assembly: TranspileAssembly]

var poket = Money.FromCents(150, Currency.Usd);

poket += new Money(250, Currency.Usd);

Console.WriteLine(poket);
