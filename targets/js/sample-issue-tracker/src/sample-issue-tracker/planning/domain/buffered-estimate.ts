/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import type { ClassType } from "metano-runtime";
import { Estimate } from "./estimate.js";

let BufferedEstimate$rt: ClassType<BufferedEstimate> | undefined;

export class BufferedEstimate extends Estimate {
  declare readonly buffer: number;

  constructor(points: number, buffer: number) {
    super(Estimate.byEstimatePoints, points);
    this.buffer = buffer;
    this.buffer = buffer;
  }

  static override get $runtimeType(): ClassType<BufferedEstimate> {
    return (BufferedEstimate$rt ??= {
      kind: "class",
      name: "BufferedEstimate",
      fullName: "SampleIssueTracker.Planning.Domain.BufferedEstimate",
      namespace: "SampleIssueTracker.Planning.Domain",
      ctor: BufferedEstimate,
      base: () => Estimate.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is BufferedEstimate => v instanceof BufferedEstimate,
    });
  }

  override addPoints(points: number): Estimate {
    return super.addPoints((points + this.buffer) | 0);
  }
}
