package com.clipboardguardian.android.services

import android.app.Notification
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.ClipData
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.SharedPreferences
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.SystemClock
import android.text.TextUtils
import android.content.ClipboardManager
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.content.edit
import com.clipboardguardian.android.ApprovalActivity
import com.clipboardguardian.android.ClipboardGuardianApp
import com.clipboardguardian.android.MainActivity
import com.clipboardguardian.android.R
import com.clipboardguardian.android.core.models.PendingRequest
import com.clipboardguardian.android.core.models.RequestType
import com.clipboardguardian.android.logging.ClipboardLogWriter
import com.google.android.material.color.MaterialColors
import java.util.UUID

class GuardianService : Service() {
    private val tag = "ClipboardGuardian"

    private val clipboardListener = ClipboardManager.OnPrimaryClipChangedListener {
        handleClipboardMutation()
    }

    private val decisionReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent == null) return
            val decision = intent.getStringExtra(EXTRA_DECISION) ?: return
            val requestId = intent.getStringExtra(EXTRA_REQUEST_ID) ?: return
            handleDecision(decision, requestId)
        }
    }

    private val readRequestReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent?.action != ACTION_REQUEST_READ) return
            val requestId = intent.getStringExtra(EXTRA_REQUEST_ID) ?: UUID.randomUUID().toString()
            handleReadRequest(requestId)
        }
    }

    private lateinit var clipboardManager: ClipboardManager
    private lateinit var logWriter: ClipboardLogWriter
    private lateinit var prefs: SharedPreferences

    private var lastApprovedText: String? = null
    private var pendingRequest: PendingRequest? = null
    private var ignoreChangesUntil: Long = 0L

    override fun onCreate() {
        super.onCreate()
        try {
            logWriter = ClipboardLogWriter(this)
            prefs = getSharedPreferences(PREFS_NAME, MODE_PRIVATE)
            clipboardManager = getSystemService(ClipboardManager::class.java)
            clipboardManager.addPrimaryClipChangedListener(clipboardListener)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                registerReceiver(decisionReceiver, IntentFilter(ACTION_DECISION), RECEIVER_NOT_EXPORTED)
                registerReceiver(readRequestReceiver, IntentFilter(ACTION_REQUEST_READ), RECEIVER_NOT_EXPORTED)
            } else {
                @Suppress("DEPRECATION")
                registerReceiver(decisionReceiver, IntentFilter(ACTION_DECISION))
                @Suppress("DEPRECATION")
                registerReceiver(readRequestReceiver, IntentFilter(ACTION_REQUEST_READ))
            }
            startAsForeground()
            isRunning = true
            persistRunningState(true)
            broadcastState(true)
            Log.i(tag, "GuardianService started")
        } catch (t: Throwable) {
            Log.e(tag, "GuardianService failed to start", t)
            try {
                persistRunningState(false)
                broadcastState(false)
            } catch (_: Throwable) {
            }
            stopSelf()
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        clipboardManager.removePrimaryClipChangedListener(clipboardListener)
        try {
            unregisterReceiver(decisionReceiver)
        } catch (_: IllegalArgumentException) {
        }
        try {
            unregisterReceiver(readRequestReceiver)
        } catch (_: IllegalArgumentException) {
        }
        logWriter.close()
        isRunning = false
        persistRunningState(false)
        broadcastState(false)
        Log.i(tag, "GuardianService stopped")
    }

    override fun onBind(intent: Intent?): IBinder? = null
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        return START_STICKY
    }

    private fun startAsForeground() {
        ensureNotificationChannel()
        val notification = buildNotification()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    private fun buildNotification(): Notification {
        val pendingIntent = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or flagImmutable()
        )
        val colorPrimary = MaterialColors.getColor(
            this,
            com.google.android.material.R.attr.colorPrimary,
            0xFF1E88E5.toInt()
        )
        return NotificationCompat.Builder(this, NOTIFICATION_CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_notification_shield)
            .setContentTitle(getString(R.string.notification_title))
            .setContentText(getString(R.string.notification_text))
            .setContentIntent(pendingIntent)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .setForegroundServiceBehavior(NotificationCompat.FOREGROUND_SERVICE_IMMEDIATE)
            .setColor(colorPrimary)
            .setColorized(true)
            .setOngoing(true)
            .build()
    }

    private fun flagImmutable(): Int {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) PendingIntent.FLAG_IMMUTABLE else 0
    }

    private fun handleClipboardMutation() {
        try {
            if (SystemClock.elapsedRealtime() < ignoreChangesUntil) return

            val clip = clipboardManager.primaryClip
            val txt = clip?.takeIf { it.itemCount > 0 }
                ?.getItemAt(0)
                ?.coerceToText(this)
                ?.toString()

            val id = UUID.randomUUID().toString()
            pendingRequest = PendingRequest(id, RequestType.WRITE, txt)

            Log.i(tag, "Clipboard changed; sample=${txt?.take(80)}")
            logWriter.log("copy", "pending", txt, "ожидание решения пользователя")

            neutralizeClipboard()

            startActivity(
                ApprovalActivity.newIntent(this, id, txt).apply {
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
                }
            )
        } catch (t: Throwable) {
            Log.e(tag, "handleClipboardMutation failed", t)
            try {
                logWriter.log("error", "failed", null, "handleClipboardMutation: ${t.javaClass.simpleName}: ${t.message}")
            } catch (_: Throwable) {
            }
        }
    }

    private fun handleReadRequest(id: String) {
        try {
            val clip = clipboardManager.primaryClip
            val txt = clip?.takeIf { it.itemCount > 0 }
                ?.getItemAt(0)
                ?.coerceToText(this)
                ?.toString()

            pendingRequest = PendingRequest(id, RequestType.READ, txt)
            logWriter.log("paste", "pending", txt, "ожидание решения пользователя (READ)")

            startActivity(
                ApprovalActivity.newReadIntent(this, id, txt).apply {
                    addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
                }
            )
        } catch (t: Throwable) {
            Log.e(tag, "handleReadRequest failed", t)
            try {
                logWriter.log("paste", "failed", null, "handleReadRequest: ${t.javaClass.simpleName}: ${t.message}")
            } catch (_: Throwable) {
            }
        }
    }

    private fun persistRunningState(running: Boolean) {
        prefs.edit { putBoolean(PREF_KEY_RUNNING, running) }
    }

    private fun broadcastState(running: Boolean) {
        sendBroadcast(
            Intent(ACTION_STATE_CHANGED).apply {
                setPackage(packageName)
                putExtra(EXTRA_IS_RUNNING, running)
            }
        )
    }

    private fun ensureNotificationChannel() {
        try {
            ClipboardGuardianApp.ensureNotificationChannel(this)
        } catch (_: Throwable) {
        }
    }

    private fun handleDecision(decision: String, id: String) {
        val req = pendingRequest ?: return
        if (!TextUtils.equals(req.id, id)) {
            return
        }
        pendingRequest = null

        when (req.type) {
            RequestType.WRITE -> handleWriteDecision(decision, req)
            RequestType.READ -> handleReadDecision(decision, req)
        }
    }

    private fun handleWriteDecision(decision: String, req: PendingRequest) {
        when (decision) {
            DECISION_ALLOW -> {
                if (!req.sample.isNullOrEmpty()) {
                    setClipboardSilently(req.sample)
                    lastApprovedText = req.sample
                }
                logWriter.log("copy", "allowed", req.sample, "пользователь разрешил")
            }
            DECISION_DENY -> {
                if (!lastApprovedText.isNullOrEmpty()) {
                    setClipboardSilently(lastApprovedText)
                } else {
                    clearClipboardSilently()
                }
                logWriter.log("copy", "blocked", req.sample, "пользователь запретил")
            }
        }
    }

    private fun handleReadDecision(decision: String, req: PendingRequest) {
        when (decision) {
            DECISION_ALLOW -> {
                sendBroadcast(
                    Intent(ACTION_READ_RESULT).apply {
                        setPackage(packageName)
                        putExtra(EXTRA_REQUEST_ID, req.id)
                        putExtra(EXTRA_SAMPLE, req.sample)
                        putExtra(EXTRA_DECISION, DECISION_ALLOW)
                    }
                )
                logWriter.log("paste", "allowed", req.sample, "пользователь разрешил (READ)")
            }
            DECISION_DENY -> {
                sendBroadcast(
                    Intent(ACTION_READ_RESULT).apply {
                        setPackage(packageName)
                        putExtra(EXTRA_REQUEST_ID, req.id)
                        putExtra(EXTRA_SAMPLE, "")
                        putExtra(EXTRA_DECISION, DECISION_DENY)
                    }
                )
                logWriter.log("paste", "blocked", req.sample, "пользователь запретил (READ)")
            }
        }
    }

    private fun neutralizeClipboard() {
        ignoreChangesUntil = SystemClock.elapsedRealtime() + 750
        clipboardManager.setPrimaryClip(ClipData.newPlainText(CLIPBOARD_LABEL, ""))
    }

    private fun setClipboardSilently(txt: String?) {
        ignoreChangesUntil = SystemClock.elapsedRealtime() + 750
        clipboardManager.setPrimaryClip(ClipData.newPlainText(CLIPBOARD_LABEL, txt ?: ""))
    }

    private fun clearClipboardSilently() {
        ignoreChangesUntil = SystemClock.elapsedRealtime() + 750
        clipboardManager.setPrimaryClip(ClipData.newPlainText(CLIPBOARD_LABEL, ""))
    }


    companion object {
        const val NOTIFICATION_CHANNEL_ID = "clipboard_guardian_channel"
        const val NOTIFICATION_ID = 42
        const val ACTION_DECISION = "com.clipboardguardian.android.ACTION_DECISION"
        const val ACTION_STATE_CHANGED = "com.clipboardguardian.android.ACTION_STATE_CHANGED"
        const val ACTION_REQUEST_READ = "com.clipboardguardian.android.ACTION_REQUEST_READ"
        const val ACTION_READ_RESULT = "com.clipboardguardian.android.ACTION_READ_RESULT"
        const val EXTRA_DECISION = "extra_decision"
        const val EXTRA_REQUEST_ID = "extra_request_id"
        const val EXTRA_SAMPLE = "extra_sample"
        const val EXTRA_IS_RUNNING = "extra_is_running"
        const val DECISION_ALLOW = "allow"
        const val DECISION_DENY = "deny"
        private const val CLIPBOARD_LABEL = "ClipboardGuardian"
        private const val PREFS_NAME = "clipboard_guardian_prefs"
        private const val PREF_KEY_RUNNING = "service_running"

        @Volatile
        var isRunning: Boolean = false

        fun readPersistedRunningState(context: Context): Boolean {
            return try {
                context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
                    .getBoolean(PREF_KEY_RUNNING, false)
            } catch (_: Throwable) {
                false
            }
        }
    }
}
