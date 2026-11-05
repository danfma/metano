/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { publicJsonShape } from "metano-runtime";
import { OBase } from "./o-base";

export class ODer extends OBase {
  readonly x: number = 0;

  private name$ODer_qoo6r7: string = "";

  constructor() {
    super();
  }

  get name(): string {
    return this.name$ODer_qoo6r7;
  }

  set name(value: string) {
    this.name$ODer_qoo6r7 = value;
  }

  toJSON(): unknown {
    return publicJsonShape(this);
  }
}
