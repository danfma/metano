import 'flag.dart';

class BclCompound {
  static String run() {
    var at = DateTime(2020, 1, 1);
    at = at.$add(TimeSpan.fromDays(1));
    var span = TimeSpan.fromHours(1);
    span = span.$subtract(TimeSpan.fromMinutes(1));
    var big = BigInt(5);
    big = big.$multiply(3);
    Decimal money = 1;
    money += 2;
    var flag = Flag(3);
    flag &= Flag(1);
    return at.toString() + span + big + money + flag.v;
  }
}
