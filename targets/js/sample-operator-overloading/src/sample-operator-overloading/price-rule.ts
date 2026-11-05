/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isBool, isInt32, isString } from "metano-runtime";
import { Decimal } from "decimal.js";
import { Currency } from "./currency";

export class PriceRule {
  protected static readonly byPriceRulePercent: unique symbol = Symbol("PriceRule(int percent)");

  protected static readonly byPriceRuleCodeTiers: unique symbol = Symbol(
    "PriceRule(string code, params int[] tiers)",
  );

  readonly description: string;

  constructor(overload: typeof PriceRule.byPriceRulePercent, percent?: number);
  constructor(overload: typeof PriceRule.byPriceRuleCodeTiers, code: string, ...tiers: number[]);
  constructor(percent?: number);
  constructor(code: string, ...tiers: number[]);
  constructor(...args: unknown[]) {
    if (typeof args[0] === "symbol" && args[0] === PriceRule.byPriceRulePercent) {
      const percent = (args[1] === undefined ? 10 : args[1]) as number;
      this.description = `${percent}% off`;

      return;
    }

    if (typeof args[0] === "symbol" && args[0] === PriceRule.byPriceRuleCodeTiers) {
      const code = args[1] as string;
      const tiers = args.slice(2) as number[];
      this.description = `${code} with ${tiers.length} tiers`;

      return;
    }

    if (args.length <= 1 && (args[0] === undefined || isInt32(args[0]))) {
      const percent = (args[0] === undefined ? 10 : args[0]) as number;
      this.description = `${percent}% off`;

      return;
    }

    if (args.length >= 1 && isString(args[0]) && args.slice(1).every((value) => isInt32(value))) {
      const code = args[0] as string;
      const tiers = args.slice(1) as number[];
      this.description = `${code} with ${tiers.length} tiers`;

      return;
    }

    throw new Error("No matching constructor");
  }

  static fromPercent(percent: number = 10): PriceRule {
    return new PriceRule(PriceRule.byPriceRulePercent, percent);
  }

  static fromCodeTiers(code: string, ...tiers: number[]): PriceRule {
    return new PriceRule(PriceRule.byPriceRuleCodeTiers, code, ...tiers);
  }

  labelUpper(upper: boolean): string {
    return upper ? this.description.toUpperCase() : this.description;
  }

  labelCurrency(currency: Currency = Currency.Usd): string {
    return currency === Currency.Usd
      ? `${this.description} in dollars`
      : `${this.description} abroad`;
  }

  label(upper: boolean): string;
  label(currency?: Currency): string;
  label(...args: unknown[]): string {
    if (args.length === 1 && isBool(args[0])) {
      return this.labelUpper(args[0] as boolean);
    }

    if (args.length <= 1 && (args[0] === undefined || isInt32(args[0]))) {
      return this.labelCurrency(args[0] as Currency);
    }

    throw new Error("No matching overload for label");
  }

  static thresholdCode(code: string): string {
    return `code ${code}`;
  }

  static thresholdUnitsMinimum(units: number, minimum: Decimal = new Decimal("1.5")): string {
    return `${units} units over ${minimum}`;
  }

  static threshold(code: string): string;
  static threshold(units: number, minimum?: Decimal): string;
  static threshold(...args: unknown[]): string {
    if (args.length === 1 && isString(args[0])) {
      return PriceRule.thresholdCode(args[0] as string);
    }

    if (
      args.length >= 1 &&
      args.length <= 2 &&
      isInt32(args[0]) &&
      (args[1] === undefined || args[1] instanceof Decimal)
    ) {
      return PriceRule.thresholdUnitsMinimum(args[0] as number, args[1] as Decimal);
    }

    throw new Error("No matching overload for threshold");
  }

  static sumLabelAmount(label: string, amount: number): number {
    return amount;
  }

  static sumAmounts(...amounts: number[]): number {
    let total = 0;
    for (const amount of amounts) {
      total += amount;
    }

    return total;
  }

  static sum(label: string, amount: number): number;
  static sum(...amounts: number[]): number;
  static sum(...args: unknown[]): number {
    if (args.length === 2 && isString(args[0]) && isInt32(args[1])) {
      return PriceRule.sumLabelAmount(args[0] as string, args[1] as number);
    }

    if (args.slice(0).every((value) => isInt32(value))) {
      return PriceRule.sumAmounts(...(args.slice(0) as number[]));
    }

    throw new Error("No matching overload for sum");
  }
}
