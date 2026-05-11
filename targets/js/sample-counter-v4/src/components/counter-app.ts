/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Component, type VNode as InfernoElement } from "inferno";
import type { EmptyProps } from "#/inferno";
import { Counter } from "#/models";
import { Button, Column, Heading, Row } from "#/mvu";

export class CounterApp extends Component<EmptyProps, Counter> {
  constructor() {
    super();
  }

  render(): InfernoElement {
    const state = this.state ?? Counter.zero;

    return Column({
      gap: 12,
      children: [Heading({ content: `Count: ${state.count}` }), Row({
        gap: 8,
        children: [Button({
          label: "➖",
          onClick: () => this.setState(state.decrement()),
        }), Button({
          label: "➕",
          onClick: () => this.setState(state.increment()),
        }), Button({
          label: "Reset",
          onClick: () => this.setState(Counter.zero),
        })],
      })],
    });
  }
}
