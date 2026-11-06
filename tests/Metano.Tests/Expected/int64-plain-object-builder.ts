import type { SnowflakeDto } from "./snowflake-dto";

export class Builder {
  constructor() { }

  static makeConst(): SnowflakeDto {
    return {
      id: 1099511627776n,
      mask: 7n,
      parent: null,
      trail: [1n, 2n],
    };
  }

  static makeWide(id: number, mask: bigint, parent: bigint | null, trail: bigint[]): SnowflakeDto {
    return {
      id: BigInt(id),
      mask: mask,
      parent: parent,
      trail: trail,
      ticks: 5n,
    };
  }

  static makeBare(id: bigint, mask: bigint, parent: bigint | null, trail: bigint[]): SnowflakeDto {
    return {
      id: id,
      mask: mask,
      parent: parent,
      trail: trail,
    };
  }
}
