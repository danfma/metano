import 'money.dart';

class Uses {
  static bool try(int value) {
    value = 4;
    return true;
  }

  static Money convert() {
    // TODO: unsupported IR statement IrTupleDeconstruction
    Money m = $found0 ? k : 0;
    return m;
  }

  static Money add(Money b) {
    // TODO: unsupported IR statement IrTupleDeconstruction
    var s = ($found0 ? q : 1) + b;
    return s;
  }

  static Money store(Money m) {
    // TODO: unsupported IR statement IrTupleDeconstruction
    m = m.$add($found0 ? r : 2);
    return m;
  }
}
