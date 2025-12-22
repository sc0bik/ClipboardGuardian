package com.clipboardguardian.android

import android.app.Application
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.SystemClock
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

internal object XposedDecisionGate {
    private const val decisionTimeoutMs = 2500L
    private const val cacheTtlMs = 1500L

    private val receiverLock = Any()
    @Volatile
    private var receiverRegistered = false

    private val pending = ConcurrentHashMap<String, CountDownLatch>()
    private val results = ConcurrentHashMap<String, Boolean>()
    private val decisionCache = ConcurrentHashMap<String, CachedDecision>()

    private val decisionReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent == null) return
            val requestId = intent.getStringExtra(GuardianService.EXTRA_REQUEST_ID) ?: return
            val decision = intent.getStringExtra(GuardianService.EXTRA_DECISION) ?: return
            results[requestId] = decision == GuardianService.DECISION_ALLOW
            pending[requestId]?.countDown()
        }
    }

    fun requestDecisionOrDeny(
        app: Application,
        targetPackage: String,
        appLabel: String?,
        mode: String,
        sample: String?
    ): Boolean {
        val now = SystemClock.elapsedRealtime()
        val cacheKey = "$targetPackage|$mode"
        decisionCache[cacheKey]?.let { cached ->
            if (now - cached.atMs <= cacheTtlMs) return cached.allowed
        }

        ensureReceiverRegistered(app)

        val requestId = UUID.randomUUID().toString()
        val latch = CountDownLatch(1)
        pending[requestId] = latch
        try {
            val intent = ApprovalActivity.newXposedIntent(
                app,
                requestId = requestId,
                mode = mode,
                targetPackage = targetPackage,
                appLabel = appLabel,
                sample = sample
            ).apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
            }
            app.startActivity(intent)
        } catch (_: Throwable) {
            pending.remove(requestId)
            return denyAndCache(cacheKey)
        }

        val finished = try {
            latch.await(decisionTimeoutMs, TimeUnit.MILLISECONDS)
        } catch (_: InterruptedException) {
            false
        }

        pending.remove(requestId)
        val allowed = if (!finished) {
            results.remove(requestId)
            false
        } else {
            results.remove(requestId) ?: false
        }

        decisionCache[cacheKey] = CachedDecision(atMs = now, allowed = allowed)
        return allowed
    }

    private fun denyAndCache(cacheKey: String): Boolean {
        decisionCache[cacheKey] = CachedDecision(atMs = SystemClock.elapsedRealtime(), allowed = false)
        return false
    }

    private fun ensureReceiverRegistered(app: Application) {
        if (receiverRegistered) return
        synchronized(receiverLock) {
            if (receiverRegistered) return
            val filter = IntentFilter(XposedContract.ACTION_DECISION_RESULT)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                app.registerReceiver(decisionReceiver, filter, Context.RECEIVER_EXPORTED)
            } else {
                @Suppress("DEPRECATION")
                app.registerReceiver(decisionReceiver, filter)
            }
            receiverRegistered = true
        }
    }

    private data class CachedDecision(
        val atMs: Long,
        val allowed: Boolean
    )
}

