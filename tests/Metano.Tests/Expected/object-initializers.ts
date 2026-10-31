import { HashSet } from "metano-runtime";
import { Bag } from "./bag";
import { Point } from "./point";
import { Tag } from "./tag";

export class Factory {
  private readonly _defaults: number[] = ((receiver$temp$0) => {
    receiver$temp$0.push(7);
    receiver$temp$0.push(8);

    return receiver$temp$0;
  })([] as Array<number>);

  seed: number = 0;

  constructor() { }

  build(capacity: number): number {
    const point = ((receiver$temp$0) => {
      receiver$temp$0.x = this.seed + 1;
      Object.assign(receiver$temp$0, { y: 2 });
      receiver$temp$0.inner.z = 3;
      receiver$temp$0.inner.items.push(4);
      receiver$temp$0.inner.items.push(5);

      return receiver$temp$0;
    })(new Point(this.seed));
    const tag = ((receiver$temp$1) => {
      Object.assign(receiver$temp$1, { name: "u" });
      Object.assign(receiver$temp$1, { weight: 1 });

      return receiver$temp$1;
    })(new Tag("t"));
    const list = ((receiver$temp$2) => {
      receiver$temp$2.push(1);
      receiver$temp$2.push(2);

      return receiver$temp$2;
    })([] as Array<number>);
    const sized = ((receiver$temp$3) => {
      receiver$temp$3.push(3);

      return receiver$temp$3;
    })([] as Array<number>);
    const typed = ((receiver$temp$4) => {
      receiver$temp$4.push("a");

      return receiver$temp$4;
    })([] as Array<string>);
    const byIndex = ((receiver$temp$5) => {
      receiver$temp$5.set("a", 1);
      receiver$temp$5.set("b", 2);

      return receiver$temp$5;
    })((new Map<string, number>()));
    const byAdd = ((receiver$temp$6) => {
      receiver$temp$6.set("c", 3);

      return receiver$temp$6;
    })((new Map<string, number>()));
    const set = ((receiver$temp$7) => {
      receiver$temp$7.add("x");
      receiver$temp$7.add("y");

      return receiver$temp$7;
    })(new HashSet<string>());
    const nested = ((receiver$temp$8) => {
      receiver$temp$8.push(((receiver$temp$9) => {
        receiver$temp$9.push(1);

        return receiver$temp$9;
      })([] as Array<number>));
      receiver$temp$8.push(((receiver$temp$10) => {
        receiver$temp$10.push(2);

        return receiver$temp$10;
      })([] as Array<number>));

      return receiver$temp$8;
    })([] as Array<number[]>);
    const bag = ((receiver$temp$11) => {
      receiver$temp$11.add("a");
      receiver$temp$11.add("b", 2);

      return receiver$temp$11;
    })(new Bag());

    return point.x + tag.weight + list.length + sized.length + typed.length + byIndex.size + byAdd.size + set.size + nested.length + this._defaults.length;
  }
}
