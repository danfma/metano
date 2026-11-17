class Uses {
  static Decimal fromInt(int i) => i;

  static Decimal half(Decimal d) => d.div(2);

  static Decimal argument(int s) => Uses.half(s);

  static Decimal sum(Decimal d, int b) => d.plus(b);

  static bool less(int u, Decimal d) => u.lt(d);

  static Decimal? lifted(int? n) => n;

  static Decimal coalesce(int? n) => n ?? 1.5;

  static Decimal cast(int s) => s as Decimal;

  static Decimal compound(Decimal d, int i) {
    d += i;
    d -= 1;
    d *= i;
    d /= 4;
    d %= 3;
    return d;
  }

  static Decimal steps(List<Decimal> arr, int k) {
    var d = arr[k]++;
    --arr[k];
    return d;
  }

  static Decimal fromLong(int l) => l;

  static bool equal(Decimal? d, int i, int l, String c) => d == Decimal(i) || d != Decimal(l) || d == Decimal(c);
}
