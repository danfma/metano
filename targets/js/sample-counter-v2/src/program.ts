/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Counter } from "./sample-counter-v2/models/counter.js";
import { App } from "./sample-counter-v2/mvu/app.js";
import { Button } from "./sample-counter-v2/mvu/widgets/button.js";
import { Column } from "./sample-counter-v2/mvu/widgets/column.js";
import { Text } from "./sample-counter-v2/mvu/widgets/text.js";

App.mount(
  "root",
  Counter.zero,
  (state: Counter, setState: (obj: Counter) => void) =>
    new Column([
      new Text(state.count.toString()),
      new Button("Click me", () => setState(state.increment())),
    ]),
);
