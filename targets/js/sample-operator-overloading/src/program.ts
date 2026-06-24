/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Currency } from "#/sample-operator-overloading/currency";
import { Money } from "#/sample-operator-overloading/money";

let poket = Money.fromCents(150n, Currency.Usd);

poket = poket.$add(new Money(250n, Currency.Usd));

console.log(poket);
