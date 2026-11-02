import { describe, expect, test } from "bun:test";
import { publicJsonShape } from "#/system/json/public-json-shape.ts";

describe("publicJsonShape", () => {
  test("renames a backing field to its property and reads it through the accessor", () => {
    class Base {
      private readonly p$Base_aaaaaa: number = 1;

      plain = "x";

      get p(): number {
        return this.p$Base_aaaaaa;
      }

      toJSON(): unknown {
        return publicJsonShape(this);
      }
    }

    expect(JSON.stringify(new Base())).toBe('{"p":1,"plain":"x"}');
  });

  test("lists a property once and lets the most derived accessor answer", () => {
    class Base {
      private readonly p$Base_aaaaaa: number = 1;

      get p(): number {
        return this.p$Base_aaaaaa;
      }

      toJSON(): unknown {
        return publicJsonShape(this);
      }
    }

    class Derived extends Base {
      private readonly p$Derived_bbbbbb: number = 2;

      override get p(): number {
        return this.p$Derived_bbbbbb;
      }
    }

    expect(JSON.stringify(new Derived())).toBe('{"p":2}');
  });

  test("passes through a property whose name only resembles a backing field", () => {
    expect(publicJsonShape({ first_name: "a", item_abcdef: 1 })).toEqual({
      first_name: "a",
      item_abcdef: 1,
    });
  });
});
