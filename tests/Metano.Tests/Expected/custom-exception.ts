import { Exception } from "metano-runtime/system/exceptions/exception";

export class InvalidAmountError extends Exception {
  constructor(message: string) {
    super(message);
  }
}
