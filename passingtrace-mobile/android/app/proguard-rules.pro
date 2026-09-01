# Optional implementations referenced by the bundled AMap SDK are not shipped
# in the combined location/search artifact. R8 generates the same suppressions.
-dontwarn com.amap.ams.gnss.GnssSoftLocator
-dontwarn net.jafama.FastMath
