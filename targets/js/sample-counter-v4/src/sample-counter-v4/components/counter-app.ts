/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { ClassType } from "metano-runtime";
import { Component, type VNode as InfernoElement } from "inferno";
import type { EmptyProps } from "../inferno/empty-props.js";
import { Counter } from "../models/counter.js";
import { Button, Column, Heading, Row } from "../mvu/ui.js";

let CounterApp$rt: ClassType<CounterApp> | undefined;

export class CounterApp extends Component<EmptyProps, Counter> {
  constructor() {
    super();
  }

  static get $runtimeType(): ClassType<CounterApp> {
    return (CounterApp$rt ??= {
      kind: "class",
      name: "CounterApp",
      fullName: "SampleCounterV4.Components.CounterApp",
      namespace: "SampleCounterV4.Components",
      ctor: CounterApp,
      interfaces: [],
      typeParameters: [],
      isAbstract: false,
      isValueType: false,
      create: () => new CounterApp(),
      test: (v: unknown): v is CounterApp => v instanceof CounterApp,
    });
  }

  render(): InfernoElement {
    const state = this.state ?? Counter.zero;

    return Column({
      gap: 12,
      children: [
        Heading({ content: `Count: ${state.count}` }),
        Row({
          gap: 8,
          children: [
            Button({
              label: "➖",
              onClick: () => this.setState(state.decrement()),
            }),
            Button({
              label: "➕",
              onClick: () => this.setState(state.increment()),
            }),
            Button({
              label: "Reset",
              onClick: () => this.setState(Counter.zero),
            }),
          ],
        }),
      ],
    });
  }
}
