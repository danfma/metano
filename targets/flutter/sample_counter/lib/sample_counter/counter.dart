class Counter {
  Counter(this.count);

  final int count;

  static Counter get zero => Counter(0);

  Counter increment() => Counter(this.count + 1);

  Counter decrement() => Counter(this.count - 1);

  @override
  bool operator ==(Object other) => other is Counter && other.runtimeType == this.runtimeType && other.count == this.count;

  @override
  int get hashCode => Object.hash(this.count);

  Counter copyWith({int? count}) => Counter(count ?? this.count);
}
