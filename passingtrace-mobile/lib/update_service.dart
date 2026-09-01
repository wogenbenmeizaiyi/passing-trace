import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:http/http.dart' as http;
import 'package:package_info_plus/package_info_plus.dart';

import 'build_environment.dart';

typedef UpdateInstaller = Future<void> Function(Map<String, Object?> request);

class AppUpdateInfo {
  const AppUpdateInfo({
    required this.updateAvailable,
    required this.required,
    required this.versionName,
    required this.versionCode,
    required this.publishedAt,
    required this.sha256,
    required this.size,
    required this.notes,
    required this.downloadUrl,
  });

  final bool updateAvailable;
  final bool required;
  final String versionName;
  final int versionCode;
  final DateTime publishedAt;
  final String sha256;
  final int size;
  final String? notes;
  final Uri? downloadUrl;

  factory AppUpdateInfo.fromJson(Map<String, dynamic> json) => AppUpdateInfo(
    updateAvailable: json['updateAvailable'] as bool? ?? false,
    required: json['required'] as bool? ?? false,
    versionName: json['versionName'] as String,
    versionCode: json['versionCode'] as int,
    publishedAt: DateTime.parse(json['publishedAt'] as String),
    sha256: json['sha256'] as String,
    size: json['size'] as int,
    notes: json['notes'] as String?,
    downloadUrl: json['downloadUrl'] == null
        ? null
        : Uri.parse(json['downloadUrl'] as String),
  );
}

class AppUpdateService {
  AppUpdateService({
    http.Client? httpClient,
    this.environment = BuildEnvironment.current,
    UpdateInstaller? installer,
  }) : _http = httpClient ?? http.Client(),
       _installer = installer ?? _installWithAndroid;

  static const _updateChannel = MethodChannel('passingtrace/app_update');

  final http.Client _http;
  final BuildEnvironment environment;
  final UpdateInstaller _installer;

  static Future<void> _installWithAndroid(Map<String, Object?> request) =>
      _updateChannel.invokeMethod<void>('downloadAndInstall', request);

  Future<AppUpdateInfo?> check({int? currentVersionCode}) async {
    if (!environment.isProduction) return null;
    final versionCode =
        currentVersionCode ??
        int.parse((await PackageInfo.fromPlatform()).buildNumber);
    final endpoint = Uri.parse(
      '${environment.eventsApiUrl}/api/v1/app-updates/android/latest',
    ).replace(queryParameters: {'currentVersionCode': '$versionCode'});
    final response = await _http
        .get(endpoint)
        .timeout(const Duration(seconds: 8));
    if (response.statusCode != 200) {
      throw Exception('检查更新失败（${response.statusCode}）');
    }
    return AppUpdateInfo.fromJson(
      jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>,
    );
  }

  Future<void> download(AppUpdateInfo update) async {
    final url = update.downloadUrl;
    if (url == null || url.scheme != 'https') {
      throw Exception('更新下载地址无效');
    }
    await _installer({
      'url': url.toString(),
      'versionName': update.versionName,
      'versionCode': update.versionCode,
      'sha256': update.sha256,
      'size': update.size,
    });
  }
}
