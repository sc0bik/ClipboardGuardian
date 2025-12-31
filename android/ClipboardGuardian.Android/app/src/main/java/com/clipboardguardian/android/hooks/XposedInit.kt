package com.clipboardguardian.android.hooks

import android.app.AndroidAppHelper
import android.content.ClipData
import android.os.SystemClock
import com.clipboardguardian.android.ApprovalActivity
import de.robv.android.xposed.IXposedHookLoadPackage
import de.robv.android.xposed.XC_MethodHook
import de.robv.android.xposed.XposedBridge
import de.robv.android.xposed.XposedHelpers
import de.robv.android.xposed.callbacks.XC_LoadPackage

class XposedInit : IXposedHookLoadPackage {

    override fun handleLoadPackage(lpparam: XC_LoadPackage.LoadPackageParam) {
        val pkg = lpparam.packageName ?: return
        if (shouldSkip(pkg)) return

        try {
            hookClipboardManager(lpparam, pkg)
        } catch (t: Throwable) {
            XposedBridge.log("ClipboardGuardian: failed to hook $pkg: ${t.javaClass.simpleName}: ${t.message}")
        }
    }

    private fun hookClipboardManager(lpparam: XC_LoadPackage.LoadPackageParam, pkg: String) {
        XposedHelpers.findAndHookMethod(
            "android.content.ClipboardManager",
            lpparam.classLoader,
            "setPrimaryClip",
            ClipData::class.java,
            object : XC_MethodHook() {
                override fun beforeHookedMethod(param: MethodHookParam) {
                    val app = AndroidAppHelper.currentApplication() ?: return
                    val label = runCatching {
                        val ai = app.packageManager.getApplicationInfo(pkg, 0)
                        app.packageManager.getApplicationLabel(ai).toString()
                    }.getOrNull()
                    val sample = extractSample(app, param.args.firstOrNull() as? ClipData)

                    val ok = XposedDecisionGate.requestDecisionOrDeny(
                        app = app,
                        targetPackage = pkg,
                        appLabel = label,
                        mode = ApprovalActivity.MODE_WRITE,
                        sample = sample
                    )
                    if (!ok) {
                        param.result = null
                    }
                }
            }
        )

        XposedHelpers.findAndHookMethod(
            "android.content.ClipboardManager",
            lpparam.classLoader,
            "getPrimaryClip",
            object : XC_MethodHook() {
                override fun beforeHookedMethod(param: MethodHookParam) {
                    val app = AndroidAppHelper.currentApplication() ?: return
                    val label = runCatching {
                        val ai = app.packageManager.getApplicationInfo(pkg, 0)
                        app.packageManager.getApplicationLabel(ai).toString()
                    }.getOrNull()

                    val ok = XposedDecisionGate.requestDecisionOrDeny(
                        app = app,
                        targetPackage = pkg,
                        appLabel = label,
                        mode = ApprovalActivity.MODE_READ,
                        sample = null
                    )
                    if (!ok) {
                        param.result = ClipData.newPlainText("ClipboardGuardian", "")
                    }
                }
            }
        )

        XposedBridge.log("ClipboardGuardian: hooks active in $pkg at ${SystemClock.elapsedRealtime()}ms")
    }

    private fun extractSample(ctx: android.content.Context, clip: ClipData?): String? {
        return try {
            val item = clip?.takeIf { it.itemCount > 0 }?.getItemAt(0)
            val txt = item?.text?.toString()
            if (!txt.isNullOrBlank()) return txt.take(160)
            item?.coerceToText(ctx)?.toString()?.take(160)
        } catch (_: Throwable) {
            null
        }
    }

    private fun shouldSkip(pkg: String): Boolean {
        if (pkg == "android") return true
        if (pkg == "com.clipboardguardian.android") return true
        if (pkg.startsWith("com.android.systemui")) return true
        if (pkg.startsWith("com.google.android.permissioncontroller")) return true
        return false
    }
}

