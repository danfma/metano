import type { Signal } from "./signal.js";
import { TextTemplate } from "./text-template.js";

export class Ui {
  private constructor() { }

  static bind(template: TextTemplate): string {
    return template.render();
  }

  static run(count: Signal<number>, n: number): string {
    let handler$temp$0, handler$temp$3;
    const a = Ui.bind((handler$temp$0 = new TextTemplate(16, 3), handler$temp$0.appendLiteral("Count: "), handler$temp$0.appendFormattedSignal<number>(count), handler$temp$0.appendLiteral(" and "), handler$temp$0.appendFormattedValueFormat<number>(n, "D3"), handler$temp$0.appendLiteral(" or "), handler$temp$0.appendFormattedValueAlignment<number>(n, 5), handler$temp$0));
    const handler$temp$1 = new TextTemplate(1, 1);
    handler$temp$1.appendLiteral("x");
    handler$temp$1.appendFormattedValue<number>(n);
    const t = handler$temp$1;
    const handler$temp$2 = new TextTemplate(2, 2);
    handler$temp$2.appendLiteral("x");
    handler$temp$2.appendFormattedValue<number>(n);
    handler$temp$2.appendLiteral("y");
    handler$temp$2.appendFormattedSignal<number>(count);
    const u = handler$temp$2;
    const c = Ui.bind((handler$temp$3 = new TextTemplate(1, 1), handler$temp$3.appendLiteral("c"), handler$temp$3.appendFormattedValue<number>(n), handler$temp$3));

    return (a ?? "") + (t.render() ?? "") + (u.render() ?? "") + (c ?? "");
  }
}
