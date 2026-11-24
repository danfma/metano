/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Currency } from "./sample-operator-overloading/currency.js";
import { Money } from "./sample-operator-overloading/money.js";

let poket = Money.fromCents(150n, Currency.Usd);

poket = Money.__add(poket, new Money(250n, Currency.Usd));

console.log(poket);
