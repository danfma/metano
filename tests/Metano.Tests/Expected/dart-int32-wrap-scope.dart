class Scope {
  int textLength(String s) => s.length.toUnsigned(32);

  int arrayLength(List<int> a) => a.length.toUnsigned(32);

  int listCount(List<int> l) => l.length.toUnsigned(32);

  int charNext(String c) => (c + 1).toUnsigned(32);

  int charUnsigned(String c) => (c as int).toSigned(32);

  int charSame(String c) => (c + 0).toUnsigned(16);

  int masked(int x) => (x & 255) as int;

  int add(int a, int b) => a + b;

  int sum(int n) {
    var s = 0;
    {
      // TODO: unsupported IR statement IrForStatement
    }
    return s;
  }
}
