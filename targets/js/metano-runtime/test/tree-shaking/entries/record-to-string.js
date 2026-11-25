// A record with int and string members and the toString / $printMembers members generated
// code emits for it (baseline FR-093 / FR-094): no runtime formatter is needed.
class Item {
  constructor(name, qty) {
    this.name = name;
    this.qty = qty;
  }

  $printMembers() {
    return `Name = ${this.name}, Qty = ${this.qty}`;
  }

  toString() {
    return `Item { ${this.$printMembers()} }`;
  }
}

export function text(name, qty) {
  return `${new Item(name, qty)}`;
}
