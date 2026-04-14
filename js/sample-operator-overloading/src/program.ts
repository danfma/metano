import { Currency, Money } from "#";

let poket = Money.fromCents(150, Currency.Usd);

poket += new Money(250, Currency.Usd);

console.log(poket);
