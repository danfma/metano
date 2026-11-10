import { firstOrDefault, linq } from "metano-runtime/system/linq";
import type { Person } from "./person";

export class Queries {
  constructor() { }

  queried(xs: Iterable<Person>): Person | null {
    return linq(xs, firstOrDefault((p: Person) => p.name === "ana", undefined, {
      tree: {
        kind: "binary",
        op: "==",
        left: {
          kind: "member",
          target: {
            kind: "param",
            name: "p",
            type: { name: "Person" },
          },
          member: "name",
        },
        right: {
          kind: "literal",
          value: "ana",
          type: { name: "string" },
        },
      },
    }));
  }

  queriedCount(xs: Iterable<number>): number {
    return linq(xs, firstOrDefault((x: number) => x > 100, 0, {
      tree: {
        kind: "binary",
        op: ">",
        left: {
          kind: "param",
          name: "x",
          type: { name: "number" },
        },
        right: {
          kind: "literal",
          value: 100,
          type: { name: "number" },
        },
      },
    }));
  }
}
