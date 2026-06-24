/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import type { VNode as InfernoElement } from "inferno";
export declare function Column(args: {
    gap?: number;
    children: InfernoElement[];
}): InfernoElement;
export declare function Row(args: {
    gap?: number;
    children: InfernoElement[];
}): InfernoElement;
export declare function Text(args: {
    content: string;
}): InfernoElement;
export declare function Heading(args: {
    content: string;
    level?: number;
}): InfernoElement;
export declare function Button(args: {
    label: string;
    onClick: () => void;
}): InfernoElement;
