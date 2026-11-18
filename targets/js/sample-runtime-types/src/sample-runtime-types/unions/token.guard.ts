/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { isNumberToken, type NumberToken } from "./number-token";
import { isTextToken, type TextToken } from "./text-token";

const variants = [() => isNumberToken, () => isTextToken];

export function isToken(value: unknown): value is NumberToken | TextToken {
  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;
  const variant = variants[["NumberToken", "TextToken"].indexOf(v.kind)];

  return variant !== undefined && variant()(value);
}

export function assertToken(
  value: unknown,
  message?: string,
): asserts value is NumberToken | TextToken {
  if (!isToken(value)) {
    throw new TypeError(message ?? "Value is not a Token");
  }
}
