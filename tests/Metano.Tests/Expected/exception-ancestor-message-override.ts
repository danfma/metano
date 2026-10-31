import { InvalidSlotException } from "./invalid-slot-exception";

export class LabelledSlotException extends InvalidSlotException {
  readonly #baseMessage: string;

  constructor() {
    super("bad slot", "slot");
    this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
    Reflect.deleteProperty(this, "message");
  }

  override get message(): string {
    return "labelled: " + this.#baseMessage;
  }
}
