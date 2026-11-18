import type { ClassType } from "metano-runtime";
import { lastOrDefault, linq } from "metano-runtime/system/linq";
import { Stage } from "@acme/domain/acme/domain";

let Pipeline$rt: ClassType<Pipeline> | undefined;

export class Pipeline {
  constructor() { }

  static get $runtimeType(): ClassType<Pipeline> {
    return (Pipeline$rt ??= {
      kind: "class",
      name: "Pipeline",
      fullName: "App.Pipeline",
      namespace: "App",
      ctor: Pipeline,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new Pipeline(),
      test: (v: unknown): v is Pipeline => v instanceof Pipeline,
    });
  }

  current(xs: Stage[]): Stage {
    return linq(xs, lastOrDefault(undefined, Stage.Draft));
  }
}
