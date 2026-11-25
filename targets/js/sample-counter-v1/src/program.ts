/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Counter } from "./sample-counter-v1/models/counter.js";
import { CounterPresenter } from "./sample-counter-v1/presenters/counter-presenter.js";
import { CounterView } from "./sample-counter-v1/views/counter-view.js";

const view = new CounterView();
const presenter = new CounterPresenter(view, Counter.zero);

presenter.startApplication("root");
