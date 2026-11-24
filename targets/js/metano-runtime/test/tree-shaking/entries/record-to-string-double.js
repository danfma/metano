// A record with a double member: its generated text pulls in the shortest round-trip
// double formatter once per program (baseline FR-094.5).
import { formatDouble } from "./system/double-format.js";

class Reading {
  constructor(name, value) {
    this.name = name;
    this.value = value;
  }

  $printMembers() {
    return `Name = ${this.name}, Value = ${formatDouble(this.value)}`;
  }

  toString() {
    return `Reading { ${this.$printMembers()} }`;
  }
}

export function text(name, value) {
  return `${new Reading(name, value)}`;
}
