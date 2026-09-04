import 'dart:math';

final _secureRandom = Random.secure();

String newStorylineKey() {
  final bytes = List<int>.generate(16, (_) => _secureRandom.nextInt(256));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  String part(int start, int length) => bytes
      .skip(start)
      .take(length)
      .map((x) => x.toRadixString(16).padLeft(2, '0'))
      .join();
  return '${part(0, 4)}-${part(4, 2)}-${part(6, 2)}-${part(8, 2)}-${part(10, 6)}';
}
