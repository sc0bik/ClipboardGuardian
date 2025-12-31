package com.clipboardguardian.android.hooks

import android.app.Application
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.SystemClock
import com.clipboardguardian.android.ApprovalActivity
import com.clipboardguardian.android.services.GuardianService
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

internal object XposedDecisionGate {
    private const val timeout = 2500L
    private const val cacheTime = 1500L

    private val lock = Any()
    @Volatile
    private var registered = false

    private val pending = ConcurrentHashMap<String, CountDownLatch>()
    private val results = ConcurrentHashMap<String, Boolean>()
    private val cache = ConcurrentHashMap<String, CachedDecision>()

    private val receiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent == null) return
            val id = intent.getStringExtra(GuardianService.EXTRA_REQUEST_ID) ?: return
            val decision = intent.getStringExtra(GuardianService.EXTRA_DECISION) ?: return
            results[id] = decision == GuardianService.DECISION_ALLOW
            pending[id]?.countDown()
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
        val key = "$targetPackage|$mode"
        cache[key]?.let { cached ->
            if (now - cached.atMs <= cacheTime) return cached.allowed
        }

        ensureRegistered(app)

        val id = UUID.randomUUID().toString()
        val latch = CountDownLatch(1)
        pending[id] = latch
        try {
            val intent = ApprovalActivity.newXposedIntent(
                app,
                requestId = id,
                mode = mode,
                targetPackage = targetPackage,
                appLabel = appLabel,
                sample = sample
            ).apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
            }
            app.startActivity(intent)
        } catch (_: Throwable) {
            pending.remove(id)
            return denyAndCache(key)
        }

        val done = try {
            latch.await(timeout, TimeUnit.MILLISECONDS)
        } catch (_: InterruptedException) {
            false
        }

        pending.remove(id)
        val allowed = if (!done) {
            results.remove(id)
            false
        } else {
            results.remove(id) ?: false
        }

        cache[key] = CachedDecision(atMs = now, allowed = allowed)
        return allowed
    }

    private fun denyAndCache(key: String): Boolean {
        cache[key] = CachedDecision(atMs = SystemClock.elapsedRealtime(), allowed = false)
        return false
    }

    private fun ensureRegistered(app: Application) {
        if (registered) return
        synchronized(lock) {
            if (registered) return
            val filter = IntentFilter(XposedContract.ACTION_DECISION_RESULT)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                app.registerReceiver(receiver, filter, Context.RECEIVER_EXPORTED)
            } else {
                @Suppress("DEPRECATION")
                app.registerReceiver(receiver, filter)
            }
            registered = true
        }
    }

    private data class CachedDecision(
        val atMs: Long,
        val allowed: Boolean
    )
}
