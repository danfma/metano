/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Counter } from "#/sample-counter-v2/models/counter";
import { App } from "#/sample-counter-v2/mvu/app";
import { Button } from "#/sample-counter-v2/mvu/widgets/button";
import { Column } from "#/sample-counter-v2/mvu/widgets/column";
import { Text } from "#/sample-counter-v2/mvu/widgets/text";

App.mount(
  "root",
  Counter.zero,
  (state: Counter, setState: (obj: Counter) => void) =>
    new Column([
      new Text(state.count.toString()),
      new Button("Click me", () => setState(state.increment())),
    ]),
);
