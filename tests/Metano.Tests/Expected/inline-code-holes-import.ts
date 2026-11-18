import { Point } from "./point";
import { createElement } from "inferno-create-element";

export class View {
  constructor() { }

  div(): Object {
    return (createElement('div')) as any;
  }

  text(): string {
    return (JSON.stringify(new Point(1, 2))) as string;
  }

  imported(): Object {
    return (createElement('span')) as any;
  }

  group(): (arg: string) => Object {
    return createElement;
  }
}
