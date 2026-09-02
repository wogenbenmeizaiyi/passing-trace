# Optional implementations referenced by the bundled AMap SDK are not shipped
# in the combined location/search artifact. R8 generates the same suppressions.
-dontwarn com.amap.ams.gnss.GnssSoftLocator
-dontwarn net.jafama.FastMath

# AMap's native map engine resolves these Java classes by their original names.
# Keep them in release builds so R8 cannot rename or remove JNI entry points.
-keep class com.amap.api.maps.** { *; }
-keep class com.autonavi.** { *; }
-keep class com.amap.api.trace.** { *; }
-keep class com.amap.api.location.** { *; }
-keep class com.amap.api.fence.** { *; }
-keep class com.loc.** { *; }
-keep class com.autonavi.aps.amapapi.model.** { *; }
-keep class com.amap.api.services.** { *; }
