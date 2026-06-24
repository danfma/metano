/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { Component, type VNode as InfernoElement } from "inferno";
import type { EmptyProps } from "#/sample-counter-v4/inferno/empty-props";
import { Counter } from "#/sample-counter-v4/models/counter";
export declare class CounterApp extends Component<EmptyProps, Counter> {
    constructor();
    render(): InfernoElement;
}
