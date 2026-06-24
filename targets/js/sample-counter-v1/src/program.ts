/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Counter } from "#/sample-counter-v1/models/counter";
import { CounterPresenter } from "#/sample-counter-v1/presenters/counter-presenter";
import { CounterView } from "#/sample-counter-v1/views/counter-view";

const view = new CounterView();
const presenter = new CounterPresenter(view, Counter.zero);

presenter.startApplication("root");
