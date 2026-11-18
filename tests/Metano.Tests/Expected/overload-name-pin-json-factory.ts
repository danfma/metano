import { isInt32, isString } from "metano-runtime";

export type Email = string & { readonly __brand: "Email" };

function Email$create(value: string): Email {
  return value as Email;
}

function Email$fromParse(value: string): Email {
  return Email.create(value);
}

function Email$fromValue(value: number): Email {
  return Email.create(value.toString());
}

function Email$from(value: string): Email;
function Email$from(value: number): Email;
function Email$from(...args: unknown[]): Email {
  if (args.length === 1 && isString(args[0])) {
    return Email.fromParse(args[0] as string);
  }

  if (args.length === 1 && isInt32(args[0])) {
    return Email.fromValue(args[0] as number);
  }

  throw new Error("No matching overload for from");
}

export const Email = {
  create: Email$create,
  fromParse: Email$fromParse,
  fromValue: Email$fromValue,
  from: Email$from,
} as const;
