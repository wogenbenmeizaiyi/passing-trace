class BuildEnvironment {
  const BuildEnvironment({
    required this.channel,
    required this.identityUrl,
    required this.eventsApiUrl,
    required this.allowEndpointOverrides,
  });

  static const _channel = String.fromEnvironment(
    'PASSINGTRACE_CHANNEL',
    defaultValue: 'internal',
  );

  static const internal = BuildEnvironment(
    channel: 'internal',
    identityUrl: String.fromEnvironment(
      'PASSINGTRACE_IDENTITY_URL',
      defaultValue: 'http://localhost:56229',
    ),
    eventsApiUrl: String.fromEnvironment(
      'PASSINGTRACE_EVENTS_API_URL',
      defaultValue: 'http://localhost:54934',
    ),
    allowEndpointOverrides: true,
  );

  static const production = BuildEnvironment(
    channel: 'production',
    identityUrl: String.fromEnvironment(
      'PASSINGTRACE_IDENTITY_URL',
      defaultValue: 'https://auth.passingtrace.com',
    ),
    eventsApiUrl: String.fromEnvironment(
      'PASSINGTRACE_EVENTS_API_URL',
      defaultValue: 'https://passingtrace.com',
    ),
    allowEndpointOverrides: false,
  );

  static const current = _channel == 'production' ? production : internal;

  final String channel;
  final String identityUrl;
  final String eventsApiUrl;
  final bool allowEndpointOverrides;

  bool get isProduction => channel == 'production';
}
