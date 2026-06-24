/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Counter } from "#/sample-counter-v3/models/counter";
import { App } from "#/sample-counter-v3/mvu/app";
import type { BuildContext } from "#/sample-counter-v3/mvu/build-context";
import { StatefulWidget } from "#/sample-counter-v3/mvu/stateful-widget";
import { Button, Column, Heading, Row } from "#/sample-counter-v3/mvu/ui";
import type { Widget } from "#/sample-counter-v3/mvu/widget";

export class CounterApp extends StatefulWidget<Counter> {
  constructor() {
    super();
  }

  initial(): Counter {
    return Counter.zero;
  }

  protected build(ctx: BuildContext<Counter>): Widget {
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
    App.run(containerId, new CounterApp());
  }
}
