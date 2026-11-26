import 'acc.dart';
import 'flag.dart';
import 'plain_pt.dart';

class Uses {
  static String run() {
    var f = Flag(1);
    var g = Flag(0);
    var t = '';
    if (f) {
      t += 'a';
    }
    while (g) {
      t += 'x';
    }
    t += f ? 'c' : 'd';
    // TODO: unsupported IR statement IrForStatement
    do {
      t += 'z';
    } while (g);
    var both = f && g;
    var either = f || g;
    var p = PlainPt(1) + PlainPt(2);
    int i = p;
    var q = 3 as PlainPt;
    var r = -p;
    p++;
    p = p.$add(PlainPt(4));
    var acc = Acc();
    acc = acc.$add(2);
    acc++;
    ++acc;
    return (t ?? '') + both.v + either.v + i + q.x + r.x + p.x + acc.v;
  }
}
