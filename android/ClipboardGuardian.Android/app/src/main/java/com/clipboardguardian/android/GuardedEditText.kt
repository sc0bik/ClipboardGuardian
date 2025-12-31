package com.clipboardguardian.android

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.util.AttributeSet
import android.view.KeyEvent
import android.widget.Toast
import com.clipboardguardian.android.services.GuardianService
import com.google.android.material.textfield.TextInputEditText
import java.util.UUID

class GuardedEditText @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = android.R.attr.editTextStyle
) : TextInputEditText(context, attrs, defStyleAttr) {

    private val mainHandler = Handler(Looper.getMainLooper())
    private var pendingReceiver: BroadcastReceiver? = null
    private var pendingRequestId: String? = null

    override fun onTextContextMenuItem(id: Int): Boolean {
        if (id == android.R.id.paste || id == android.R.id.pasteAsPlainText) {
            return requestPasteAndConsume()
        }
        return super.onTextContextMenuItem(id)
    }

    override fun onKeyShortcut(keyCode: Int, event: KeyEvent): Boolean {
        if (keyCode == KeyEvent.KEYCODE_V && event.isCtrlPressed) {
            return requestPasteAndConsume()
        }
        return super.onKeyShortcut(keyCode, event)
    }

    private fun requestPasteAndConsume(): Boolean {
        val isRunning = GuardianService.readPersistedRunningState(context)
        if (!isRunning) {
            Toast.makeText(context, R.string.toast_start_service_for_test, Toast.LENGTH_SHORT).show()
            return true
        }

        if (pendingReceiver != null) return true

        val requestId = "paste-${UUID.randomUUID()}"
        pendingRequestId = requestId

        val receiver = object : BroadcastReceiver() {
            override fun onReceive(ctx: Context?, intent: Intent?) {
                if (intent?.action != GuardianService.ACTION_READ_RESULT) return
                val incomingId = intent.getStringExtra(GuardianService.EXTRA_REQUEST_ID) ?: return
                if (incomingId != requestId) return
                cleanupReceiver()

                val decision = intent.getStringExtra(GuardianService.EXTRA_DECISION)
                val sample = intent.getStringExtra(GuardianService.EXTRA_SAMPLE) ?: ""

                if (decision == GuardianService.DECISION_ALLOW) {
                    insertText(sample)
                }
            }
        }

        pendingReceiver = receiver
        val filter = IntentFilter(GuardianService.ACTION_READ_RESULT)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            context.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED)
        } else {
            @Suppress("DEPRECATION")
            context.registerReceiver(receiver, filter)
        }

        mainHandler.postDelayed({ cleanupReceiver() }, 7000)

        context.sendBroadcast(
            Intent(GuardianService.ACTION_REQUEST_READ).apply {
                setPackage(context.packageName)
                putExtra(GuardianService.EXTRA_REQUEST_ID, requestId)
            }
        )

        return true
    }

    private fun insertText(value: String) {
        val editable = text ?: return
        val start = selectionStart.coerceAtLeast(0)
        val end = selectionEnd.coerceAtLeast(0)
        val min = minOf(start, end)
        val max = maxOf(start, end)
        editable.replace(min, max, value)
        setSelection((min + value.length).coerceAtMost(editable.length))
    }

    private fun cleanupReceiver() {
        val receiver = pendingReceiver ?: return
        pendingReceiver = null
        pendingRequestId = null
        try {
            context.unregisterReceiver(receiver)
        } catch (_: IllegalArgumentException) {
        }
    }
}
