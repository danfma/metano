import 'package:flutter/material.dart';

import 'sample_counter/counter.dart';
import 'sample_counter/counter_presenter.dart';
import 'sample_counter/i_counter_view.dart';

void main() {
  runApp(const SampleCounterApp());
}

class SampleCounterApp extends StatelessWidget {
  const SampleCounterApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'Sample Counter',
        theme: ThemeData.light(useMaterial3: true),
        home: const CounterScreen(),
      );
}

class CounterScreen extends StatefulWidget {
  const CounterScreen({super.key});

  @override
  State<CounterScreen> createState() => _CounterScreenState();
}

/// The Flutter widget state implements the generated [ICounterView] interface
/// and delegates increment/decrement to the generated [CounterPresenter],
/// mirroring the MVP architecture of the SampleCounter C# project exactly.
class _CounterScreenState extends State<CounterScreen> implements ICounterView {
  late final CounterPresenter _presenter = CounterPresenter(this);
  Counter _counter = Counter.zero;

  @override
  void displayCounter(Counter counter) {
    setState(() {
      _counter = counter;
    });
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('Sample Counter')),
        body: Center(
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              const Text('You have pushed the button this many times:'),
              Text(
                '${_counter.count}',
                style: Theme.of(context).textTheme.headlineMedium,
              ),
            ],
          ),
        ),
        floatingActionButton: Row(
          mainAxisAlignment: MainAxisAlignment.end,
          children: [
            FloatingActionButton(
              heroTag: 'decrement',
              onPressed: _presenter.decrement,
              tooltip: 'Decrement',
              child: const Icon(Icons.remove),
            ),
            const SizedBox(width: 8),
            FloatingActionButton(
              heroTag: 'increment',
              onPressed: _presenter.increment,
              tooltip: 'Increment',
              child: const Icon(Icons.add),
            ),
          ],
        ),
      );
}
