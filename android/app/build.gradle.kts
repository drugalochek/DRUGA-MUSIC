import java.util.Base64

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.sonora.app"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.sonora.app"
        minSdk = 26
        targetSdk = 34
        versionCode = (System.getenv("SONORA_VERSION_CODE") ?: "1").toInt()
        versionName = "0.3.0"
    }

    // подпись: если в окружении есть ключ (секреты GitHub), Gradle подписывает сам; иначе APK подписывается после сборки
    val ksB64 = System.getenv("SONORA_KEYSTORE_B64")
    if (!ksB64.isNullOrBlank()) {
        val ksFile = layout.buildDirectory.file("sonora-release.jks").get().asFile
        ksFile.parentFile.mkdirs()
        ksFile.writeBytes(Base64.getDecoder().decode(ksB64.trim()))
        signingConfigs {
            create("release") {
                storeFile = ksFile
                storePassword = System.getenv("SONORA_KS_PASS")
                keyAlias = System.getenv("SONORA_KEY_ALIAS") ?: "sonora"
                keyPassword = System.getenv("SONORA_KEY_PASS") ?: System.getenv("SONORA_KS_PASS")
            }
        }
    }

    buildTypes {
        release {
            if (!ksB64.isNullOrBlank()) signingConfig = signingConfigs.getByName("release")
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true; buildConfig = true }
    lint { checkReleaseBuilds = false; abortOnError = false }
    packaging { resources { excludes += "/META-INF/{AL2.0,LGPL2.1}" } }
}

dependencies {
    val bom = platform("androidx.compose:compose-bom:2024.09.02")
    implementation(bom)
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.activity:activity-compose:1.9.2")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.media3:media3-exoplayer:1.4.1")
    implementation("androidx.media3:media3-session:1.4.1")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
    // вход через Google: системное окно выбора аккаунта (Credential Manager)
    implementation("androidx.credentials:credentials:1.3.0")
    implementation("androidx.credentials:credentials-play-services-auth:1.3.0")
    implementation("com.google.android.libraries.identity.googleid:googleid:1.1.1")
}
