/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";

let Blob$rt: ClassType<Blob> | undefined;

export class Blob {
  static get $runtimeType(): ClassType<Blob> {
    return (Blob$rt ??= {
      kind: "class",
      name: "Blob",
      fullName: "SampleRuntimeTypes.Shapes.Blob",
      namespace: "SampleRuntimeTypes.Shapes",
      ctor: Blob,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Blob(),
      test: (v: unknown): v is Blob => v instanceof Blob,
    });
  }

  get size(): number {
    return 1;
  }
}
