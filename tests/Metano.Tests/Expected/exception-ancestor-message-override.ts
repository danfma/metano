import { InvalidSlotException } from "./invalid-slot-exception";

export class LabelledSlotException extends InvalidSlotException {
  override readonly name: string = "LabelledSlotException";
  readonly #baseMessage: string;

  constructor() {
    super("bad slot", "slot");
    this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
    Reflect.deleteProperty(this, "message");
  }

  protected override get $typeName(): string {
    return "LabelledSlotException";
  }

  protected override get $typeNamespace(): string {
    return "App";
  }

  override get message(): string {
    return "labelled: " + this.#baseMessage;
  }
}
