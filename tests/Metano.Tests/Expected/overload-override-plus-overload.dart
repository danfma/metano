// base.dart
class Base {
  String addX(int x) => 'int';

  String addS(String s) => 'string';
}

// circle.dart
import 'shape.dart';

class Circle extends Shape {
  String drawX(int x) => 'int';

  String drawS(String s) => 'string';

  String drawB(bool b) => 'bool';
}

// clash.dart
import 'base.dart';

class Clash extends Base {
  String addS(String s) => 'clash';

  String addInt64(int x) => 'long';
}

// derived.dart
import 'base.dart';

class Derived extends Base {
  String addX(int x) => 'derived:' + super.addX(x);

  String addB(bool b) => 'bool';
}

// hider.dart
import 'base.dart';

class Hider extends Base {
  String addX$(int x) => 'hidden';

  String addB(bool b) => 'bool';
}

// i_sink.dart
abstract interface class ISink {
  String writeX(int x);

  String writeS(String s);
}

// leaf.dart
import 'middle.dart';

class Leaf extends Middle {
  String addX(int x) => 'leaf';

  String addD(double d) => 'leaf:' + super.addD(d);

  String addB(bool b) => 'bool';
}

// middle.dart
import 'base.dart';

class Middle extends Base {
  String addS(String s) => 'middle';

  String addD(double d) => 'double';
}

// repo.dart
class Repo<T> {
  String putItem(T item) => 'item';

  String putCount(int count) => 'count';
}

// shape.dart
abstract class Shape {
  String drawX(int x);

  String drawS(String s);
}

// sink.dart
import 'i_sink.dart';

class Sink implements ISink {
  String writeX(int x) => 'int';

  String writeS(String s) => 'string';

  String writeB(bool b) => 'bool';
}

// str_repo.dart
import 'repo.dart';

class StrRepo extends Repo<String> {
  String putItem(String item) => 'str:' + item;

  String putFlag(bool flag) => 'flag';
}
