package com.passingtrace.passingtrace_mobile

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.location.LocationListener
import android.location.LocationManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import com.amap.api.location.AMapLocationClient
import com.amap.api.location.AMapLocationClientOption
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    private val channelName = "passingtrace/amap_location"
    private val permissionRequest = 7312
    private val mapPickerRequest = 7313
    private var permissionResult: MethodChannel.Result? = null
    private var mapPickerResult: MethodChannel.Result? = null
    private var locationClient: AMapLocationClient? = null
    private var systemLocationManager: LocationManager? = null
    private var systemLocationListener: LocationListener? = null
    private var systemLocationTimeout: Runnable? = null
    private val mainHandler = Handler(Looper.getMainLooper())

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
        super.onDestroy()
    }
}
