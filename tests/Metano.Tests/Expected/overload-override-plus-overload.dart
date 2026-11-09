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

// flag_mapper.dart
import 'mapper.dart';

class FlagMapper extends Mapper {
  String mapX<T>(T x) => 'flag:' + super.mapX(x);

  String mapBC(bool b, bool c) => 'flags';
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

  String addS(String s) => 'leaf:' + super.addS(s);

  String addD(double d) => 'leaf:' + super.addD(d);

  String addB(bool b) => 'bool';
}

// mapper.dart
class Mapper {
  String mapX<T>(T x) => 'generic';

  String mapXY(int x, int y) => 'pair';
}

// middle.dart
import 'base.dart';

class Middle extends Base {
  String addS(String s) => 'middle';

  String addD(double d) => 'double';
}

// renamed.dart
import 'base.dart';

class Renamed extends Base {
  String addX(int y) => 'renamed';

  String addY(bool y) => 'bool';
}

// renamed_clash.dart
import 'base.dart';

class RenamedClash extends Base {
  String addX(int y) => 'renamed';

  String addBoolean(bool x) => 'bool';
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
