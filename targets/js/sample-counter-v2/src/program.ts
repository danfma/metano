/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Counter } from "#/models";
import { App } from "#/mvu";
import { Button, Column, Text } from "#/mvu/widgets";

App.mount("root", Counter.zero, (state: Counter, setState: (obj: Counter) => void) => new Column([new Text(state.count.toString()), new Button("Click me", () => setState(state.increment()))]));
