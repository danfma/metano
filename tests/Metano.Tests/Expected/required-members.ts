import { Point } from "./point.js";

export class Person {
  declare name: string;
  declare nick: string;
  declare readonly age: number;
  declare title: string;
  declare home: Point;
  declare note: string | null;

  constructor() {
    this.name = undefined!;
    this.nick = undefined!;
    this.age = 0;
    this.title = "Mx";
    this.home = new Point();
    this.note = null;
  }
}
