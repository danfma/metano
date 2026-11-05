import { linq, toArray, where } from "metano-runtime/system/linq";
import type { Row } from "./row";

export class Rows {
  constructor() { }

  filter(rows: Iterable<Row>, cap: bigint, limit: number): Row[] {
    return linq(rows, where((r: Row) => r.size > cap && r.count < 5n && BigInt(r.count) === 2n && r.weight > Number(cap) && r.size < limit && ((receiver$temp$0) => receiver$temp$0 != null && receiver$temp$0 > 10n)(r.optional == null ? null : BigInt.asIntN(64, r.optional + BigInt(r.count))), {
      tree: {
        kind: "let",
        bindings: [
          {
            name: "$0",
            value: {
              kind: "member",
              target: {
                kind: "param",
                name: "r",
                type: { name: "Row" },
              },
              member: "size",
            },
          },
          {
            name: "$1",
            value: {
              kind: "member",
              target: {
                kind: "param",
                name: "r",
                type: { name: "Row" },
              },
              member: "count",
            },
          },
          {
            name: "$2",
            value: {
              kind: "convert",
              operand: { kind: "ref", name: "$1" },
              type: { name: "System.Int64" },
            },
          },
        ],
        body: {
          kind: "binary",
          op: "&&",
          left: {
            kind: "binary",
            op: "&&",
            left: {
              kind: "binary",
              op: "&&",
              left: {
                kind: "binary",
                op: "&&",
                left: {
                  kind: "binary",
                  op: "&&",
                  left: {
                    kind: "binary",
                    op: ">",
                    left: { kind: "ref", name: "$0" },
                    right: {
                      kind: "capture",
                      name: "cap",
                      type: { name: "System.Int64" },
                    },
                  },
                  right: {
                    kind: "binary",
                    op: "<",
                    left: { kind: "ref", name: "$2" },
                    right: {
                      kind: "literal",
                      value: 5n,
                      type: { name: "System.Int64" },
                    },
                  },
                },
                right: {
                  kind: "binary",
                  op: "==",
                  left: { kind: "ref", name: "$2" },
                  right: {
                    kind: "literal",
                    value: 2n,
                    type: { name: "System.Int64" },
                  },
                },
              },
              right: {
                kind: "binary",
                op: ">",
                left: {
                  kind: "member",
                  target: {
                    kind: "param",
                    name: "r",
                    type: { name: "Row" },
                  },
                  member: "weight",
                },
                right: {
                  kind: "convert",
                  operand: {
                    kind: "capture",
                    name: "cap",
                    type: { name: "System.Int64" },
                  },
                  type: { name: "number" },
                },
              },
            },
            right: {
              kind: "binary",
              op: "<",
              left: { kind: "ref", name: "$0" },
              right: {
                kind: "convert",
                operand: {
                  kind: "capture",
                  name: "limit",
                  type: { name: "number" },
                },
                type: { name: "System.Int64" },
              },
            },
          },
          right: {
            kind: "binary",
            op: ">",
            left: {
              kind: "binary",
              op: "+",
              left: {
                kind: "member",
                target: {
                  kind: "param",
                  name: "r",
                  type: { name: "Row" },
                },
                member: "optional",
              },
              right: {
                kind: "convert",
                operand: { kind: "ref", name: "$1" },
                type: { name: "System.Int64" },
              },
            },
            right: {
              kind: "literal",
              value: 10n,
              type: { name: "System.Int64" },
            },
          },
        },
      },
      captures: { cap: cap, limit: limit },
    }), toArray());
  }
}
