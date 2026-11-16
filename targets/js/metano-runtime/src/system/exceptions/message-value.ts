/**
 * A value as an exception message quotes it (`Actual value was <value>.`, the
 * argument guards' `'<value>'`): JavaScript's `String`, except that `-0` keeps its
 * sign, as .NET prints it. Other values read as JavaScript prints them, which can
 * differ from .NET's formatting (`true` for `True`, an ISO date).
 */
export function messageValue(value: unknown): string {
  return Object.is(value, -0) ? "-0" : String(value);
}
