/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { CounterGroup } from "./counter-group";
import { render } from "solid-js/web";

const container = document.getElementById("root") ?? document.body;

render(() => <CounterGroup />, container);
