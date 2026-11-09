/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
/** biome-ignore-all lint/suspicious/noShadowRestrictedNames: the branded companions keep the C# member names, and a long-branded type synthesizes toString over a bigint */
import { Estimate } from "./estimate";

export class BufferedEstimate extends Estimate {
  constructor(
    points: number,
    readonly buffer: number,
  ) {
    super(Estimate.byEstimatePoints, points);
    this.buffer = buffer;
  }

  override addPoints(points: number): Estimate {
    return super.addPoints(points + this.buffer);
  }
}
