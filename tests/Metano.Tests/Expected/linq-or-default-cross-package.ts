import { lastOrDefault, linq } from "metano-runtime/system/linq";
import { Stage } from "@acme/domain/acme/domain";

export class Pipeline {
  constructor() { }

  current(xs: Stage[]): Stage {
    return linq(xs, lastOrDefault(undefined, Stage.Draft));
  }
}
