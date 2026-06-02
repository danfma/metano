import { CounterGroup } from "./counter-group";
import { render } from "solid-js/web";

const container = document.getElementById("root") ?? document.body;

render(() => <CounterGroup />, container);
