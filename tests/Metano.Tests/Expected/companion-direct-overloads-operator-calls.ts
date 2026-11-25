import { Ticks } from "./ticks.js";
import type { UId } from "./u-id.js";

export class OperatorCalls {
  run(ticks: Ticks, id: UId): Ticks {
    return Ticks.__addTicksUId(Ticks.__addTicksInt64(ticks, 2n), id);
  }
}
