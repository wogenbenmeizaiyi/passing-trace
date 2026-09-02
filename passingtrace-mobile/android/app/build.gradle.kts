import java.util.Properties

plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

val localProperties = Properties().apply {
    val file = rootProject.file("local.properties")
    if (file.exists()) file.inputStream().use { load(it) }
}

val releaseStoreFile = providers.environmentVariable("ANDROID_SIGNING_STORE_FILE")
    .orElse(localProperties.getProperty("ANDROID_SIGNING_STORE_FILE", ""))
    .get()
val releaseStorePassword = providers.environmentVariable("ANDROID_SIGNING_STORE_PASSWORD")
    .orElse(localProperties.getProperty("ANDROID_SIGNING_STORE_PASSWORD", ""))
    .get()
val releaseKeyAlias = providers.environmentVariable("ANDROID_SIGNING_KEY_ALIAS")
    .orElse(localProperties.getProperty("ANDROID_SIGNING_KEY_ALIAS", ""))
    .get()
val releaseKeyPassword = providers.environmentVariable("ANDROID_SIGNING_KEY_PASSWORD")
    .orElse(localProperties.getProperty("ANDROID_SIGNING_KEY_PASSWORD", ""))
    .get()
val releaseSigningConfigured = listOf(
    releaseStoreFile,
    releaseStorePassword,
    releaseKeyAlias,
    releaseKeyPassword,
).all(String::isNotBlank)

android {
    namespace = "com.passingtrace.passingtrace_mobile"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "com.passingtrace.passingtrace_mobile"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        // Authorization callbacks are handled by app_links in MainActivity.
        // AppAuth is only used for token requests, so its receiver uses an unused scheme.
        manifestPlaceholders["appAuthRedirectScheme"] = "com.passingtrace.mobile.appauth-unused"
        manifestPlaceholders["amapApiKey"] = localProperties.getProperty("AMAP_ANDROID_KEY", "")
        // Uses the version code from pubspec.yaml. When using split APKs, 1000 * ABI_VERSION
        // is added automatically by Flutter. (https://developer.android.com/studio/build/configure-apk-splits#configure-APK-versions)
        // You can force using the value of versionCode by specifying the `-P force-version-code-ignoring-abi=true`
        // flag during build.
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    signingConfigs {
        create("productionRelease") {
            if (releaseSigningConfigured) {
                storeFile = file(releaseStoreFile)
                storePassword = releaseStorePassword
                keyAlias = releaseKeyAlias
                keyPassword = releaseKeyPassword
            }
        }
    }

    flavorDimensions += "environment"
    productFlavors {
        create("internal") {
            dimension = "environment"
            applicationIdSuffix = ".internal"
            manifestPlaceholders["appLabel"] = "星期八·内测"
        }
        create("production") {
            dimension = "environment"
            manifestPlaceholders["appLabel"] = "星期八"
        }
    }

    buildTypes {
        release {
            signingConfig = signingConfigs.getByName("productionRelease")
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }
    }

    if (
        gradle.startParameter.taskNames.any { it.contains("Release", ignoreCase = true) } &&
        !releaseSigningConfigured
    ) {
        throw GradleException(
            "Release signing is not configured. Set ANDROID_SIGNING_STORE_FILE, " +
                "ANDROID_SIGNING_STORE_PASSWORD, ANDROID_SIGNING_KEY_ALIAS and " +
                "ANDROID_SIGNING_KEY_PASSWORD."
        )
    }
}

dependencies {
    implementation("com.amap.api:3dmap-location-search:11.2.100_loc11.2.100_sea9.8.1")
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}
