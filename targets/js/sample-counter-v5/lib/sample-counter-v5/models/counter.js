/** biome-ignore-all lint/complexity/noUselessConstructor: explicit shape preserved by transpiler */
import { HashCode } from "metano-runtime";
export class Counter {
    count;
    static zero = new Counter(0);
    constructor(count) {
        this.count = count;
    }
    increment() {
        return this.with({ count: this.count + 1 });
    }
    decrement() {
        return this.with({ count: this.count - 1 });
    }
    equals(other) {
        return other instanceof Counter && this.count === other.count;
    }
    hashCode() {
        const hc = new HashCode();
        hc.add(this.count);
        return hc.toHashCode();
    }
    with(overrides) {
        return new Counter(overrides?.count ?? this.count);
    }
}
