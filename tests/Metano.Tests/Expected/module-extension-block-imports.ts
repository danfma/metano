import { plainInt32, plainString, size$get, twice } from "./block-host.js";

export class UseBlock {
  go(): number {
    return (twice("x") + size$get("x") + plainInt32(1) + plainString("y")) | 0;
  }
}
