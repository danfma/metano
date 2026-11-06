/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
export type SnowflakeId = bigint & { readonly __brand: "SnowflakeId" };

export namespace SnowflakeId {
  export function create(value: bigint): SnowflakeId {
    return value as SnowflakeId;
  }

  export function toString(value: SnowflakeId): string {
    return String(value);
  }

  export function from(value: bigint): SnowflakeId {
    if (value <= 0n) {
      throw new Error("A snowflake id must be positive.");
    }

    return SnowflakeId.create(value);
  }

  export function timestampMilliseconds(id: SnowflakeId): bigint {
    return BigInt.asIntN(64, BigInt.asUintN(64, id) >> 22n);
  }
}
