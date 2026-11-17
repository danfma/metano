// i_eq.dart
abstract interface class IEq<T> {
  bool equals(T o);
}

// ----
// pt.dart
import 'package:metano_runtime/metano_runtime.dart' show HashCode;

class Pt {
  Pt(int x) {
    this.x = x;
  }

  late int x;
}

// ----
// s_e.dart
import 'package:metano_runtime/metano_runtime.dart' show HashCode;
import 'i_eq.dart';

class SE implements IEq<SE> {
  SE(int x) {
    this.x = x;
  }

  late int x;

  bool equals(SE o) => o.x == this.x;
}

// ----
// s_expl.dart
import 'package:metano_runtime/metano_runtime.dart' show HashCode;

class SExpl {
  SExpl(int x) {
    this.x = x;
  }

  late int x;
}

// ----
// s_obj.dart
class SObj {
  SObj(int x) {
    this.x = x;
  }

  late int x;

  bool equals(Object? o) {
    SObj s;
    return /* TODO: unsupported is-pattern IrTypePattern */ && s.x == this.x;
  }

  @override
  bool operator ==(Object other) => this.equals(other);

  @override
  int get hashCode => this.x;
}
