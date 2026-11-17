import 'money.dart';
import 'pt.dart';

class Uses {
  static Money arithmetic(Money a, Money b) => a + b + -a + 3;

  static bool equality(Money a, Money? b) => a == b || b == null || null != a;

  static int store(Money m, List<Money> arr, int i) {
    m = m.$add(2);
    m++;
    var before = m++;
    arr[i++] = arr[i++].$add(m);
    return before as int;
  }

  static Decimal toDecimal(Money m) {
    Decimal d = m;
    d += m;
    return d;
  }

  static Pt? lifted(Pt? a, Pt b, Pt? c) => a == c ? a == null ? null : a + b : null;

  static Money sum(List<int> raw) {
    Money total = 0;
    for (Money m in raw) {
      total = total.$add(m);
    }
    return total;
  }
}
