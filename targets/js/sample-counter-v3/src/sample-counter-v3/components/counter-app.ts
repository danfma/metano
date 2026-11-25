/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Counter } from "../models/counter.js";
import { App } from "../mvu/app.js";
import type { BuildContext } from "../mvu/build-context.js";
import { StatefulWidget } from "../mvu/stateful-widget.js";
import { Button, Column, Heading, Row } from "../mvu/ui.js";
import type { Widget } from "../mvu/widget.js";

export class CounterApp extends StatefulWidget<Counter> {
  constructor() {
    super();
  }

  override initial(): Counter {
    return Counter.zero;
  }

  protected override build(ctx: BuildContext<Counter>): Widget {
    return Column({
      gap: 12,
      children: [
        Heading({ content: `Count: ${ctx.state.count}` }),
        Row({
          gap: 8,
          children: [
            Button({
              label: "➖",
              onPressed: () => ctx.setState((s: Counter) => s.decrement()),
            }),
            Button({
              label: "➕",
              onPressed: () => ctx.setState((s: Counter) => s.increment()),
            }),
            Button({
              label: "Reset",
              onPressed: () => ctx.setState((_: Counter) => Counter.zero),
            }),
          ],
        }),
      ],
    });
  }

  static mount(containerId: string): void {
    App.run<Counter>(containerId, new CounterApp());
  }
}
