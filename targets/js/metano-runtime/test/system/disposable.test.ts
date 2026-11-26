import { describe, expect, test } from "bun:test";
import {
  disposeResource,
  type IDisposable,
  IDisposable$runtimeType,
} from "#/system/disposable.ts";
import type { ClassType } from "#/system/reflection/index.ts";

let Resource$rt: ClassType<Resource> | undefined;

/** A class shaped like the compiler's output for `class Resource : IDisposable`. */
class Resource implements IDisposable {
  disposed = 0;

  static get $runtimeType(): ClassType<Resource> {
    return (Resource$rt ??= {
      kind: "class",
      name: "Resource",
      fullName: "App.Resource",
      namespace: "App",
      ctor: Resource,
      interfaces: [() => IDisposable$runtimeType],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      test: (v: unknown): v is Resource => v instanceof Resource,
    });
  }

  dispose(): void {
    this.disposed++;
  }

  [Symbol.dispose](): void {
    disposeResource(this, this.dispose);
  }
}

/** Derives from an implementer without a descriptor of its own. */
class DerivedResource extends Resource {}

describe("IDisposable$runtimeType", () => {
  test("describes System.IDisposable", () => {
    expect(IDisposable$runtimeType.kind).toBe("interface");
    expect(IDisposable$runtimeType.fullName).toBe("System.IDisposable");
  });

  test("accepts an instance whose class descriptor lists the interface", () => {
    expect(IDisposable$runtimeType.test(new Resource())).toBe(true);
    expect(IDisposable$runtimeType.test(new DerivedResource())).toBe(true);
  });

  test("rejects a value that only has a dispose method", () => {
    const lookalike = { dispose: () => {} };

    expect(IDisposable$runtimeType.test(lookalike)).toBe(false);
    expect(IDisposable$runtimeType.test(null)).toBe(false);
    expect(IDisposable$runtimeType.test("s")).toBe(false);
  });
});

describe("disposeResource", () => {
  test("runs the given method on the resource", () => {
    const resource = new Resource();

    disposeResource(resource, resource.dispose);

    expect(resource.disposed).toBe(1);
  });

  test("lets a using declaration dispose a generated-shape implementer", () => {
    const resource = new Resource();

    {
      using _held = resource;
    }

    expect(resource.disposed).toBe(1);
  });
});
