/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { linq, select, where } from "metano-runtime/system/linq";
import type { User } from "./user";

export class UserQueries {
  constructor() { }

  static adults(users: Iterable<User>): Iterable<User> {
    return linq(users, where((u: User) => u.age >= 18, {
      tree: {
        kind: "binary",
        op: ">=",
        left: {
          kind: "member",
          target: {
            kind: "param",
            name: "u",
            type: { name: "User" },
          },
          member: "age",
        },
        right: {
          kind: "literal",
          value: 18,
          type: { name: "number" },
        },
      },
    }));
  }

  static activeAdults(users: Iterable<User>): Iterable<User> {
    return linq(users, where((u: User) => u.age >= 18 && u.active, {
      tree: {
        kind: "binary",
        op: "&&",
        left: {
          kind: "binary",
          op: ">=",
          left: {
            kind: "member",
            target: {
              kind: "param",
              name: "u",
              type: { name: "User" },
            },
            member: "age",
          },
          right: {
            kind: "literal",
            value: 18,
            type: { name: "number" },
          },
        },
        right: {
          kind: "member",
          target: {
            kind: "param",
            name: "u",
            type: { name: "User" },
          },
          member: "active",
        },
      },
    }));
  }

  static adultsAtLeast(users: Iterable<User>, minAge: number): Iterable<User> {
    return linq(users, where((u: User) => u.age >= minAge, {
      tree: {
        kind: "binary",
        op: ">=",
        left: {
          kind: "member",
          target: {
            kind: "param",
            name: "u",
            type: { name: "User" },
          },
          member: "age",
        },
        right: {
          kind: "capture",
          name: "minAge",
          type: { name: "number" },
        },
      },
      captures: { minAge: minAge },
    }));
  }

  static adultNames(users: Iterable<User>): Iterable<string> {
    return linq(users, where((u: User) => u.age >= 18, {
      tree: {
        kind: "binary",
        op: ">=",
        left: {
          kind: "member",
          target: {
            kind: "param",
            name: "u",
            type: { name: "User" },
          },
          member: "age",
        },
        right: {
          kind: "literal",
          value: 18,
          type: { name: "number" },
        },
      },
    }), select((u: User) => u.name, {
      tree: {
        kind: "member",
        target: {
          kind: "param",
          name: "u",
          type: { name: "User" },
        },
        member: "name",
      },
    }));
  }
}
