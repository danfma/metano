import { ArgumentException } from "./exceptions/argument-exception.ts";
import { formatText } from "./default-text.ts";

/**
 * The `ArgumentException` of `Math.Clamp` and `INumber<T>.Clamp` when `min` is greater than
 * `max`, with .NET's message. The bounds are formatted as .NET formats them (a `decimal`
 * `0.0000001` stays `0.0000001`, a `double` follows `double.ToString()`).
 */
export function throwMinGreaterThanMax(min: unknown, max: unknown): never {
  throw new ArgumentException(`'${formatText(min)}' cannot be greater than ${formatText(max)}.`);
}
