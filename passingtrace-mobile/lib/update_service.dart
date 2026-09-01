import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:package_info_plus/package_info_plus.dart';
import 'package:url_launcher/url_launcher.dart';

import 'build_environment.dart';

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
  }) : _http = httpClient ?? http.Client();

  final http.Client _http;
  final BuildEnvironment environment;

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
    if (url == null ||
        !await launchUrl(url, mode: LaunchMode.externalApplication)) {
      throw Exception('无法打开更新下载地址');
    }
  }
}
