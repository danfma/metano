class Lists {
  int nullable() {
    List<int?> l = [1];
    l.add(null);
    return l.length;
  }

  int empty() {
    List<int> l = [];
    return l.length;
  }

  int readOnly() {
    Iterable<int?> l = [1];
    return l.length;
  }
}
