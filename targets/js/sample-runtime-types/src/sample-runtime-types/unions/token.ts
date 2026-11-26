/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode, copyRecordWith, type ClassType, type EnumType } from "metano-runtime";

export const TokenKind = {
  NumberToken: "NumberToken",
  TextToken: "TextToken",
} as const;

export type TokenKind = (typeof TokenKind)[keyof typeof TokenKind];

export const TokenKind$runtimeType: EnumType<TokenKind> = {
  kind: "stringEnum",
  name: "TokenKind",
  fullName: "SampleRuntimeTypes.Unions.TokenKind",
  namespace: "SampleRuntimeTypes.Unions",
  values: ["NumberToken", "TextToken"],
  names: ["NumberToken", "TextToken"],
  isFlags: false,
  default: () => "NumberToken",
  test: (v: unknown): v is TokenKind => v === "NumberToken" || v === "TextToken",
};

let Token$rt: ClassType<Token> | undefined;

export abstract class Token {
  declare readonly kind: TokenKind;

  constructor(kind: TokenKind) {
    this.kind = kind;
  }

  static get $runtimeType(): ClassType<Token> {
    return (Token$rt ??= {
      kind: "class",
      name: "Token",
      fullName: "SampleRuntimeTypes.Unions.Token",
      namespace: "SampleRuntimeTypes.Unions",
      ctor: Token,
      interfaces: [],
      typeParameters: [],
      isAbstract: true,
      isValueType: false,
      test: (v: unknown): v is Token => v instanceof Token,
    });
  }

  equals(other: any): boolean {
    return (
      other != null &&
      Object.getPrototypeOf(other) === Object.getPrototypeOf(this) &&
      this.kind === other.kind
    );
  }

  hashCode(): number {
    const hc = new HashCode();
    hc.add(this.kind);

    return hc.toHashCode();
  }

  with(overrides?: Partial<Token>): Token {
    return copyRecordWith<Token>(this, overrides);
  }

  protected $printMembers(): string {
    return `Kind = ${this.kind}`;
  }

  toString(): string {
    return `Token { ${this.$printMembers()} }`;
  }
}
