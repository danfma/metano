export interface SnowflakeDto {
  readonly id: bigint;
  readonly mask: bigint;
  readonly parent: bigint | null;
  readonly trail: bigint[];
  readonly ticks?: bigint;
}
