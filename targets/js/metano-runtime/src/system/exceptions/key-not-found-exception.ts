import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.Collections.Generic.KeyNotFoundException()` carries on .NET. */
const KEY_NOT_FOUND_EXCEPTION_MESSAGE = "The given key was not present in the dictionary.";

let KeyNotFoundException$rt: ClassType<KeyNotFoundException> | undefined;

/** Mirrors `System.Collections.Generic.KeyNotFoundException`: a key that a dictionary does not contain. */
export class KeyNotFoundException extends SystemException {
  override readonly name: string = "KeyNotFoundException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? KEY_NOT_FOUND_EXCEPTION_MESSAGE, innerException);
  }

  protected override get $typeNamespace(): string {
    return "System.Collections.Generic";
  }

  /**
   * The runtime type descriptor of `System.Collections.Generic.KeyNotFoundException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<KeyNotFoundException> {
    return (KeyNotFoundException$rt ??= {
      kind: "class",
      name: "KeyNotFoundException",
      fullName: "System.Collections.Generic.KeyNotFoundException",
      namespace: "System.Collections.Generic",
      ctor: KeyNotFoundException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new KeyNotFoundException(),
      test: (value: unknown): value is KeyNotFoundException =>
        value instanceof KeyNotFoundException,
    });
  }
}
