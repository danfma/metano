import type { ClassType } from "metano-runtime";
import { InvalidSlotException } from "./invalid-slot-exception";

let LabelledSlotException$rt: ClassType<LabelledSlotException> | undefined;

export class LabelledSlotException extends InvalidSlotException {
  override readonly name: string = "LabelledSlotException";
  readonly #baseMessage: string;

  constructor() {
    super("bad slot", "slot");
    this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
    Reflect.deleteProperty(this, "message");
  }

  static override get $runtimeType(): ClassType<LabelledSlotException> {
    return (LabelledSlotException$rt ??= {
      kind: "class",
      name: "LabelledSlotException",
      fullName: "App.LabelledSlotException",
      namespace: "App",
      ctor: LabelledSlotException,
      base: () => InvalidSlotException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new LabelledSlotException(),
      test: (v: unknown): v is LabelledSlotException => v instanceof LabelledSlotException,
    });
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
