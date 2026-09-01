package com.passingtrace.passingtrace_mobile

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.app.DownloadManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.location.LocationListener
import android.location.LocationManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.widget.Toast
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import com.amap.api.location.AMapLocationClient
import com.amap.api.location.AMapLocationClientOption
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import java.io.File
import java.io.FileInputStream
import java.security.MessageDigest

class MainActivity : FlutterActivity() {
    private val channelName = "passingtrace/amap_location"
    private val updateChannelName = "passingtrace/app_update"
    private val permissionRequest = 7312
    private val mapPickerRequest = 7313
    private var permissionResult: MethodChannel.Result? = null
    private var mapPickerResult: MethodChannel.Result? = null
    private var locationClient: AMapLocationClient? = null
    private var systemLocationManager: LocationManager? = null
    private var systemLocationListener: LocationListener? = null
    private var systemLocationTimeout: Runnable? = null
    private var updateReceiverRegistered = false
    private var updateDownloadId: Long? = null
    private var updateApkFile: File? = null
    private var updateExpectedSha256: String? = null
    private var updateExpectedSize = 0L
    private var pendingInstallApk: File? = null
    private val mainHandler = Handler(Looper.getMainLooper())
    private val updateDownloadReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent?.action != DownloadManager.ACTION_DOWNLOAD_COMPLETE) return
            val completedId = intent.getLongExtra(DownloadManager.EXTRA_DOWNLOAD_ID, -1L)
            if (completedId == updateDownloadId) handleUpdateDownloadCompleted(completedId)
        }
    }

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, channelName).setMethodCallHandler { call, result ->
            when (call.method) {
                "requestPermission" -> requestLocationPermission(result)
                "locateOnce" -> locateOnce(call.argument<Boolean>("privacyAccepted") == true, result)
                "pickMapPoint" -> openMapPicker(
                    call.argument<Double>("latitude"),
                    call.argument<Double>("longitude"),
                    call.argument<Boolean>("privacyAccepted") == true,
                    result
                )
                "dispose" -> { destroyLocation(); result.success(null) }
                else -> result.notImplemented()
            }
        }
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, updateChannelName).setMethodCallHandler { call, result ->
            when (call.method) {
                "downloadAndInstall" -> startUpdateDownload(
                    call.argument<String>("url"),
                    call.argument<String>("versionName"),
                    call.argument<Number>("versionCode")?.toLong(),
                    call.argument<String>("sha256"),
                    call.argument<Number>("size")?.toLong(),
                    result
                )
                else -> result.notImplemented()
            }
        }
        if (!updateReceiverRegistered) {
            ContextCompat.registerReceiver(
                this,
                updateDownloadReceiver,
                IntentFilter(DownloadManager.ACTION_DOWNLOAD_COMPLETE),
                ContextCompat.RECEIVER_EXPORTED
            )
            updateReceiverRegistered = true
        }
    }

    private fun startUpdateDownload(
        rawUrl: String?,
        versionName: String?,
        versionCode: Long?,
        sha256: String?,
        expectedSize: Long?,
        result: MethodChannel.Result
    ) {
        if (updateDownloadId != null) {
            result.error("UPDATE_BUSY", "更新已在下载中。", null)
            return
        }
        val url = rawUrl?.let(Uri::parse)
        if (url?.scheme != "https" || versionCode == null || versionCode <= 0 ||
            sha256 == null || !sha256.matches(Regex("^[0-9a-fA-F]{64}$")) ||
            expectedSize == null || expectedSize <= 0
        ) {
            result.error("INVALID_UPDATE", "更新信息不完整。", null)
            return
        }

        try {
            val downloads = getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS)
                ?: throw IllegalStateException("无法使用下载目录。")
            val updateDirectory = File(downloads, "updates").apply { mkdirs() }
            val destination = File(updateDirectory, "PassingTrace-$versionCode.apk")
            if (destination.exists() && !destination.delete()) {
                throw IllegalStateException("无法替换旧的更新文件。")
            }

            val manager = getSystemService(Context.DOWNLOAD_SERVICE) as DownloadManager
            val request = DownloadManager.Request(url)
                .setTitle("星期八 ${versionName ?: versionCode} 更新")
                .setDescription("正在下载安装包")
                .setMimeType(APK_MIME_TYPE)
                .setAllowedOverMetered(true)
                .setAllowedOverRoaming(false)
                .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                .setDestinationInExternalFilesDir(
                    this,
                    Environment.DIRECTORY_DOWNLOADS,
                    "updates/${destination.name}"
                )
            updateApkFile = destination
            updateExpectedSha256 = sha256.lowercase()
            updateExpectedSize = expectedSize
            updateDownloadId = manager.enqueue(request)
            result.success(null)
        } catch (error: Exception) {
            clearUpdateDownload()
            result.error("UPDATE_DOWNLOAD", error.message ?: "无法启动更新下载。", null)
        }
    }

    private fun handleUpdateDownloadCompleted(downloadId: Long) {
        val manager = getSystemService(Context.DOWNLOAD_SERVICE) as DownloadManager
        val status = manager.query(DownloadManager.Query().setFilterById(downloadId)).use { cursor ->
            if (!cursor.moveToFirst()) DownloadManager.STATUS_FAILED
            else cursor.getInt(cursor.getColumnIndexOrThrow(DownloadManager.COLUMN_STATUS))
        }
        updateDownloadId = null
        if (status != DownloadManager.STATUS_SUCCESSFUL) {
            showUpdateMessage("更新下载失败，请稍后重试。")
            clearUpdateDownload()
            return
        }

        val apk = updateApkFile
        val expectedHash = updateExpectedSha256
        val expectedSize = updateExpectedSize
        if (apk == null || expectedHash == null) {
            showUpdateMessage("更新文件状态异常，请重新下载。")
            clearUpdateDownload()
            return
        }

        Thread {
            val error = validateDownloadedApk(apk, expectedSize, expectedHash)
            mainHandler.post {
                if (error != null) {
                    apk.delete()
                    showUpdateMessage(error)
                } else {
                    requestPackageInstall(apk)
                }
                updateApkFile = null
                updateExpectedSha256 = null
                updateExpectedSize = 0L
            }
        }.start()
    }

    private fun validateDownloadedApk(file: File, expectedSize: Long, expectedHash: String): String? {
        if (!file.isFile || file.length() != expectedSize) return "更新文件大小校验失败。"
        return try {
            val digest = MessageDigest.getInstance("SHA-256")
            FileInputStream(file).use { input ->
                val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
                while (true) {
                    val count = input.read(buffer)
                    if (count < 0) break
                    digest.update(buffer, 0, count)
                }
            }
            val actualHash = digest.digest().joinToString("") { "%02x".format(it) }
            if (actualHash == expectedHash) null else "更新文件完整性校验失败。"
        } catch (_: Exception) {
            "无法校验更新文件。"
        }
    }

    private fun requestPackageInstall(apk: File) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && !packageManager.canRequestPackageInstalls()) {
            pendingInstallApk = apk
            showUpdateMessage("请允许星期八安装未知应用，返回后会继续安装。")
            startActivity(
                Intent(
                    Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                    Uri.parse("package:$packageName")
                )
            )
            return
        }
        launchPackageInstaller(apk)
    }

    private fun launchPackageInstaller(apk: File) {
        val contentUri = FileProvider.getUriForFile(
            this,
            "$packageName.update-file-provider",
            apk
        )
        startActivity(
            Intent(Intent.ACTION_INSTALL_PACKAGE).apply {
                data = contentUri
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                putExtra(Intent.EXTRA_NOT_UNKNOWN_SOURCE, true)
            }
        )
    }

    private fun clearUpdateDownload() {
        updateDownloadId = null
        updateApkFile = null
        updateExpectedSha256 = null
        updateExpectedSize = 0L
    }

    private fun showUpdateMessage(message: String) {
        Toast.makeText(this, message, Toast.LENGTH_LONG).show()
    }

    override fun onResume() {
        super.onResume()
        val apk = pendingInstallApk ?: return
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O || packageManager.canRequestPackageInstalls()) {
            pendingInstallApk = null
            launchPackageInstaller(apk)
        }
    }

    private fun openMapPicker(
        latitude: Double?,
        longitude: Double?,
        privacyAccepted: Boolean,
        result: MethodChannel.Result
    ) {
        if (!privacyAccepted) {
            result.error("PRIVACY_REQUIRED", "使用地图前需要同意位置隐私说明。", null)
            return
        }
        if (latitude == null || longitude == null) {
            result.error("LOCATION_REQUIRED", "地图选点缺少初始位置。", null)
            return
        }
        if (mapPickerResult != null) {
            result.error("MAP_PICKER_BUSY", "地图选点已经打开。", null)
            return
        }
        mapPickerResult = result
        startActivityForResult(
            Intent(this, MapPickerActivity::class.java).apply {
                putExtra(MapPickerActivity.EXTRA_LATITUDE, latitude)
                putExtra(MapPickerActivity.EXTRA_LONGITUDE, longitude)
            },
            mapPickerRequest
        )
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != mapPickerRequest) return
        val result = mapPickerResult ?: return
        mapPickerResult = null
        if (resultCode != Activity.RESULT_OK || data == null) {
            result.success(null)
            return
        }
        result.success(mapOf(
            "latitude" to data.getDoubleExtra(MapPickerActivity.EXTRA_LATITUDE, 0.0),
            "longitude" to data.getDoubleExtra(MapPickerActivity.EXTRA_LONGITUDE, 0.0)
        ))
    }

    private fun requestLocationPermission(result: MethodChannel.Result) {
        if (hasLocationPermission()) { result.success(true); return }
        permissionResult = result
        ActivityCompat.requestPermissions(this,
            arrayOf(Manifest.permission.ACCESS_COARSE_LOCATION, Manifest.permission.ACCESS_FINE_LOCATION),
            permissionRequest)
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == permissionRequest) {
            permissionResult?.success(hasLocationPermission())
            permissionResult = null
        }
    }

    private fun locateOnce(privacyAccepted: Boolean, result: MethodChannel.Result) {
        if (!privacyAccepted) { result.error("PRIVACY_REQUIRED", "使用定位前需要同意位置隐私说明。", null); return }
        if (!hasLocationPermission()) { result.error("PERMISSION_DENIED", "未授予前台定位权限。", null); return }
        destroyLocation()
        if (isEmulator()) {
            locateFromEmulatorGps(result)
            return
        }
        try {
            AMapLocationClient.updatePrivacyShow(applicationContext, true, true)
            AMapLocationClient.updatePrivacyAgree(applicationContext, true)
            val client = AMapLocationClient(applicationContext)
            locationClient = client
            client.setLocationOption(AMapLocationClientOption().apply {
                locationMode = AMapLocationClientOption.AMapLocationMode.Hight_Accuracy
                isOnceLocation = true
                isOnceLocationLatest = true
                isNeedAddress = false
                httpTimeOut = 10000
            })
            client.setLocationListener { location ->
                if (location.errorCode == 0) {
                    result.success(mapOf(
                        "latitude" to location.latitude,
                        "longitude" to location.longitude,
                        "accuracyMeters" to location.accuracy.toDouble(),
                        "capturedAt" to location.time
                    ))
                } else result.error("AMAP_${location.errorCode}", location.errorInfo ?: "定位失败", null)
                destroyLocation()
            }
            client.startLocation()
        } catch (error: Exception) {
            destroyLocation()
            result.error("AMAP_INIT", error.message ?: "高德定位初始化失败", null)
        }
    }

    @SuppressLint("MissingPermission")
    private fun locateFromEmulatorGps(result: MethodChannel.Result) {
        val manager = getSystemService(Context.LOCATION_SERVICE) as LocationManager
        systemLocationManager = manager
        val listener = LocationListener { location ->
            if (systemLocationListener == null) return@LocationListener
            result.success(mapOf(
                "latitude" to location.latitude,
                "longitude" to location.longitude,
                "accuracyMeters" to location.accuracy.toDouble(),
                "capturedAt" to location.time
            ))
            destroySystemLocation()
        }
        systemLocationListener = listener
        val timeout = Runnable {
            if (systemLocationListener != null) {
                result.error(
                    "EMULATOR_LOCATION_UNAVAILABLE",
                    "模拟器还没有虚拟位置，请在 Extended controls > Location 中设置位置。",
                    null
                )
                destroySystemLocation()
            }
        }
        systemLocationTimeout = timeout
        try {
            manager.requestSingleUpdate(LocationManager.GPS_PROVIDER, listener, Looper.getMainLooper())
            mainHandler.postDelayed(timeout, 10000)
        } catch (error: Exception) {
            destroySystemLocation()
            result.error("EMULATOR_LOCATION", error.message ?: "无法读取模拟器虚拟位置。", null)
        }
    }

    private fun hasLocationPermission(): Boolean =
        ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_COARSE_LOCATION) == PackageManager.PERMISSION_GRANTED ||
            ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION) == PackageManager.PERMISSION_GRANTED

    private fun isEmulator(): Boolean =
        Build.FINGERPRINT.startsWith("generic") ||
            Build.FINGERPRINT.contains("emulator") ||
            Build.MODEL.contains("Emulator") ||
            Build.MODEL.contains("Android SDK built for") ||
            Build.MANUFACTURER.contains("Genymotion") ||
            Build.PRODUCT.contains("sdk_gphone")

    private fun destroyLocation() {
        locationClient?.stopLocation()
        locationClient?.onDestroy()
        locationClient = null
        destroySystemLocation()
    }

    private fun destroySystemLocation() {
        systemLocationTimeout?.let(mainHandler::removeCallbacks)
        systemLocationTimeout = null
        systemLocationListener?.let { listener ->
            systemLocationManager?.removeUpdates(listener)
        }
        systemLocationListener = null
        systemLocationManager = null
    }

    override fun onDestroy() {
        destroyLocation()
        mapPickerResult?.success(null)
        mapPickerResult = null
        if (updateReceiverRegistered) {
            unregisterReceiver(updateDownloadReceiver)
            updateReceiverRegistered = false
        }
        super.onDestroy()
    }

    companion object {
        private const val APK_MIME_TYPE = "application/vnd.android.package-archive"
    }
}
