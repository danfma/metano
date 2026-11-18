/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, valueEquals, type ClassType } from "metano-runtime";
import { Token, type TokenKind } from "./token";

let NumberToken$rt: ClassType<NumberToken> | undefined;

export class NumberToken extends Token {
  constructor(
    kind: TokenKind,
    readonly value: number,
  ) {
    super(kind);
  }

  static override get $runtimeType(): ClassType<NumberToken> {
    return (NumberToken$rt ??= {
      kind: "class",
      name: "NumberToken",
      fullName: "SampleRuntimeTypes.Unions.NumberToken",
      namespace: "SampleRuntimeTypes.Unions",
      ctor: NumberToken,
      base: () => Token.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is NumberToken => v instanceof NumberToken,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && valueEquals(this.value, other.value);
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.value);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<NumberToken>): NumberToken {
    return copyRecordWith<NumberToken>(this, overrides);
  }
}

export function isNumberToken(value: unknown): value is NumberToken {
  if (value instanceof NumberToken) {
    return true;
  }

  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.value === "number" && v.kind != null;
}

export function assertNumberToken(value: unknown, message?: string): asserts value is NumberToken {
  if (!isNumberToken(value)) {
    throw new TypeError(message ?? "Value is not a NumberToken");
  }
}
