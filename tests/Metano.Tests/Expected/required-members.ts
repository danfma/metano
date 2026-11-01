import { Point } from "./point";

export class Person {
  name!: string;

  nick!: string;

  readonly age: number = 0;

  title: string = "Mx";

  home: Point = new Point();

  note: string | null = null;

  constructor() { }
}
