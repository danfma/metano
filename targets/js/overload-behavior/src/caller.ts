/** biome-ignore-all lint/complexity/noBannedTypes: explicit shape preserved by transpiler */
/** biome-ignore-all lint/correctness/noUnusedFunctionParameters: explicit shape preserved by transpiler */
/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { LoudGreeter } from "./loud-greeter";

export class Caller {
  constructor() {}

  static throughBase(): number {
    const g = new LoudGreeter();

    return g.greetNameTimes("x", 3);
  }

  static direct(): number {
    const g = new LoudGreeter();

    return g.greetNameTimes$("x", 9);
  }
}
