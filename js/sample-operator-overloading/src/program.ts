import { Currency } from "./currency";
import { Money } from "./money";

let poket = Money.fromCents(150, Currency.Usd);

poket += new Money(250, Currency.Usd);

console.log(poket);
