/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Counter } from "#/models";
import { App, Button, Column, Heading, Row, StatefulWidget, type BuildContext, type Widget } from "#/mvu";

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
      children: [Heading({ content: `Count: ${ctx.state.count}` }), Row({
        gap: 8,
        children: [Button({
          label: "➖",
          onPressed: () => ctx.setState((s: Counter) => s.decrement()),
        }), Button({
          label: "➕",
          onPressed: () => ctx.setState((s: Counter) => s.increment()),
        }), Button({
          label: "Reset",
          onPressed: () => ctx.setState((_: Counter) => Counter.zero),
        })],
      })],
    });
  }

  static mount(containerId: string): void {
    App.run(containerId, new CounterApp());
  }
}
