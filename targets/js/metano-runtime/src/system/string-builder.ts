import { ArgumentOutOfRangeException } from "./exceptions/index.ts";

/**
 * `System.Text.StringBuilder`: a mutable text. Every mutator returns the builder, so calls
 * chain as they do in C#. `AppendLine` adds `\n` (`Environment.NewLine` on Unix); an index
 * or count outside the text throws `ArgumentOutOfRangeException`.
 */
export class StringBuilder {
  private text: string;

  constructor(initial?: string | null) {
    this.text = initial ?? "";
  }

  /** The number of characters; assigning truncates, or pads with NUL characters. */
  get length(): number {
    return this.text.length;
  }

  set length(value: number) {
    if (value < 0) {
      throw new ArgumentOutOfRangeException("value", value, "Length cannot be less than zero.");
    }

    this.text =
      value <= this.text.length
        ? this.text.slice(0, value)
        : this.text + "\0".repeat(value - this.text.length);
  }

  /** `Append(value)`: `null` appends nothing. */
  append(value: string | null | undefined): this {
    this.text += value ?? "";

    return this;
  }

  /** `Append(char, repeatCount)`. */
  appendRepeated(value: string, repeatCount: number): this {
    if (repeatCount < 0) {
      throw new ArgumentOutOfRangeException(
        "repeatCount",
        repeatCount,
        `repeatCount ('${repeatCount}') must be a non-negative value.`,
      );
    }

    this.text += value.repeat(repeatCount);

    return this;
  }

  /** `AppendLine(value)`. */
  appendLine(value?: string | null): this {
    this.text += `${value ?? ""}\n`;

    return this;
  }

  /** `Clear()`. */
  clear(): this {
    this.text = "";

    return this;
  }

  /** `Insert(index, value)`. */
  insert(index: number, value: string | null | undefined): this {
    requireInRange(index, 0, this.text.length, "index");
    this.text = this.text.slice(0, index) + (value ?? "") + this.text.slice(index);

    return this;
  }

  /** `Remove(startIndex, length)`. */
  remove(startIndex: number, length: number): this {
    requireInRange(length, 0, this.text.length, "length");
    requireInRange(startIndex, 0, this.text.length - length, "startIndex");
    this.text = this.text.slice(0, startIndex) + this.text.slice(startIndex + length);

    return this;
  }

  /** `Replace(oldValue, newValue)`: every occurrence, literally. */
  replace(oldValue: string, newValue: string | null | undefined): this {
    if (oldValue === "") {
      throw new ArgumentOutOfRangeException("oldValue", oldValue, "The value cannot be empty.");
    }

    this.text = this.text.split(oldValue).join(newValue ?? "");

    return this;
  }

  /** `ToString()`. */
  toString(): string {
    return this.text;
  }

  /** `ToString(startIndex, length)`. */
  toSubstring(startIndex: number, length: number): string {
    requireInRange(length, 0, this.text.length, "length");
    requireInRange(startIndex, 0, this.text.length - length, "startIndex");

    return this.text.slice(startIndex, startIndex + length);
  }
}

const INDEX_AT_MOST_COUNT_MESSAGE =
  "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";

/** An index or length outside the text. */
function requireInRange(value: number, min: number, max: number, parameter: string): void {
  if (value < min || value > max) {
    throw new ArgumentOutOfRangeException(parameter, undefined, INDEX_AT_MOST_COUNT_MESSAGE);
  }
}
