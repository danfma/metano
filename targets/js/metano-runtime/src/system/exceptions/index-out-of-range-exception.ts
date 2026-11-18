import { SystemException } from "./system-exception.ts";
import type { ClassType } from "../reflection/runtime-type.ts";

/** The message `new System.IndexOutOfRangeException()` carries on .NET. */
const INDEX_OUT_OF_RANGE_EXCEPTION_MESSAGE = "Index was outside the bounds of the array.";

let IndexOutOfRangeException$rt: ClassType<IndexOutOfRangeException> | undefined;

/** Mirrors `System.IndexOutOfRangeException`: an array index outside its bounds. */
export class IndexOutOfRangeException extends SystemException {
  override readonly name: string = "IndexOutOfRangeException";

  constructor(message?: string | null, innerException?: Error | null) {
    super(message ?? INDEX_OUT_OF_RANGE_EXCEPTION_MESSAGE, innerException);
  }

  /**
   * The runtime type descriptor of `System.IndexOutOfRangeException`: a data literal built on first read,
   * so an unused class costs nothing and a thrown one pulls in no reflection code.
   */
  static override get $runtimeType(): ClassType<IndexOutOfRangeException> {
    return (IndexOutOfRangeException$rt ??= {
      kind: "class",
      name: "IndexOutOfRangeException",
      fullName: "System.IndexOutOfRangeException",
      namespace: "System",
      ctor: IndexOutOfRangeException,
      base: () => SystemException.$runtimeType,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new IndexOutOfRangeException(),
      test: (value: unknown): value is IndexOutOfRangeException =>
        value instanceof IndexOutOfRangeException,
    });
  }
}
