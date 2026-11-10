import { isInt32, isString } from "metano-runtime";

export type Email = string & { readonly __brand: "Email" };

export namespace Email {
  export function create(value: string): Email {
    return value as Email;
  }

  export function fromParse(value: string): Email {
    return Email.create(value);
  }

  export function fromValue(value: number): Email {
    return Email.create(value.toString());
  }

  export function from(value: string): Email;
  export function from(value: number): Email;
  export function from(...args: unknown[]): Email {
    if (args.length === 1 && isString(args[0])) {
      return fromParse(args[0] as string);
    }

    if (args.length === 1 && isInt32(args[0])) {
      return fromValue(args[0] as number);
    }

    throw new Error("No matching overload for from");
  }
}
