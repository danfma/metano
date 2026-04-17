import { Currency } from "./currency";
import { Money } from "./money";

let poket = Money.fromCents(150n, Currency.Usd);

poket = poket.$add(new Money(250n, Currency.Usd));

console.log(poket);
