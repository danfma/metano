/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType } from "metano-runtime";
import { Token, type TokenKind } from "./token.js";

let TextToken$rt: ClassType<TextToken> | undefined;

export class TextToken extends Token {
  constructor(
    kind: TokenKind,
    readonly text: string,
  ) {
    super(kind);
  }

  static override get $runtimeType(): ClassType<TextToken> {
    return (TextToken$rt ??= {
      kind: "class",
      name: "TextToken",
      fullName: "SampleRuntimeTypes.Unions.TextToken",
      namespace: "SampleRuntimeTypes.Unions",
      ctor: TextToken,
      base: () => Token.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is TextToken => v instanceof TextToken,
    });
  }

  override equals(other: any): boolean {
    return super.equals(other) && this.text === other.text;
  }

  override hashCode(): number {
    const hc = new HashCode();
    hc.add(super.hashCode());
    hc.add(this.text);

    return hc.toHashCode();
  }

  override with(overrides?: Partial<TextToken>): TextToken {
    return copyRecordWith<TextToken>(this, overrides);
  }

  protected override $printMembers(): string {
    const base = super.$printMembers?.() ?? "";

    return `${base ? `${base}, ` : ""}Text = ${this.text}`;
  }

  override toString(): string {
    return `TextToken { ${this.$printMembers()} }`;
  }
}

export function isTextToken(value: unknown): value is TextToken {
  if (value instanceof TextToken) {
    return true;
  }

  if (value == null || typeof value !== "object") {
    return false;
  }

  const v = value as any;

  return typeof v.text === "string" && v.kind != null;
}

export function assertTextToken(value: unknown, message?: string): asserts value is TextToken {
  if (!isTextToken(value)) {
    throw new TypeError(message ?? "Value is not a TextToken");
  }
}
