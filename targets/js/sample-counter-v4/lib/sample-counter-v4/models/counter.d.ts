export declare class Counter {
    readonly count: number;
    static readonly zero: Counter;
    constructor(count: number);
    increment(): Counter;
    decrement(): Counter;
    equals(other: any): boolean;
    hashCode(): number;
    with(overrides?: Partial<Counter>): Counter;
}
