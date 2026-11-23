import 'package:metano_runtime/metano_runtime.dart' show HashCode;

final class Counter {
  Counter(this.count);

  final int count;

  static final Counter zero = Counter(0);

  Counter increment() => this.copyWith(count: this.count + 1);

  @override
  bool operator ==(Object other) => other is Counter && other.runtimeType == this.runtimeType && other.count == this.count;

  @override
  int get hashCode => HashCode.combine(this.count);

  Counter copyWith({int? count}) => Counter(count ?? this.count);
}
