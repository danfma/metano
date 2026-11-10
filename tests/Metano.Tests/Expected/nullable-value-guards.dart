class Unwraps {
  int checkedLocal(int? n) {
    if (n != null) {
      return n;
    }
    return -1;
  }

  int unchecked(int? n) => n ?? (throw InvalidOperationException('Nullable object must have a value.'));

  int suppressed(int? n) => n;

  int checkedCast(double? d) => d != null ? (d as double).toInt().toSigned(32) : -1;

  int uncheckedCast(double? d) => ((d ?? (throw InvalidOperationException('Nullable object must have a value.'))) as double).toInt().toSigned(32);

  int wrappedConstant() => 5 as int?;

  int disabled(int? n) => n;
}
