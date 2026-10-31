import { describe, expect, test } from "bun:test";
import { copyRecordWith } from "#/system/record-copy.ts";

class Shape {
  constructor(readonly name: string | null) {}

  describe(): string {
    return `shape ${this.name}`;
  }
}

class Square extends Shape {
  tag: string | null = "default";

  constructor(readonly side: number) {
    super("square");
  }

  get area(): number {
    return this.side * this.side;
  }
}

describe("copyRecordWith", () => {
  test("keeps the prototype and every own field", () => {
    const square = new Square(2);
    const copy = copyRecordWith<Square>(square);

    expect(copy).not.toBe(square);
    expect(copy).toBeInstanceOf(Square);
    expect(copy.side).toBe(2);
    expect(copy.name).toBe("square");
    expect(copy.tag).toBe("default");
    expect(copy.describe()).toBe("shape square");
  });

  test("applies overrides of members the constructor does not take", () => {
    const copy = copyRecordWith<Square>(new Square(2), { name: "renamed" });

    expect(copy.name).toBe("renamed");
    expect(copy.side).toBe(2);
  });

  test("assigns null but keeps the copied value for undefined", () => {
    const copy = copyRecordWith<Square>(new Square(2), { tag: null, side: undefined });

    expect(copy.tag).toBeNull();
    expect(copy.side).toBe(2);
  });

  test("leaves the source untouched", () => {
    const square = new Square(2);

    copyRecordWith<Square>(square, { side: 5 });

    expect(square.side).toBe(2);
  });

  test("an empty override object yields an equal copy", () => {
    const copy = copyRecordWith<Square>(new Square(3), {});

    expect(copy.side).toBe(3);
    expect(copy.name).toBe("square");
  });

  test("skips an override of a getter-only property instead of throwing", () => {
    const copy = copyRecordWith<Square>(new Square(3), { area: 100 });

    expect(copy.area).toBe(9);
  });

  test("sets a constructor field to null", () => {
    const copy = copyRecordWith<Shape>(new Shape("circle"), { name: null });

    expect(copy.name).toBeNull();
  });
});
