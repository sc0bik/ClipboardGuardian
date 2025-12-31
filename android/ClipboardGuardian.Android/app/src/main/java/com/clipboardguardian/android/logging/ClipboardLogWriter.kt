package com.clipboardguardian.android.logging

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import org.json.JSONObject
import java.io.File
import java.time.Instant

class ClipboardLogWriter(context: Context) {
    private val logDir: File = File(context.filesDir, "logs").apply { mkdirs() }
    private val logFile: File = File(logDir, "clipboard_log.ndjson")
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    fun log(action: String, decision: String, sample: String?, note: String) {
        val json = JSONObject().apply {
            put("timestamp", Instant.now().toString())
            put("action", action)
            put("decision", decision)
            if (!sample.isNullOrBlank()) {
                put("sample", sample.take(200))
            }
            put("note", note)
        }.toString()

        scope.launch {
            synchronized(logFile) {
                logFile.appendText(json + "\n")
            }
        }
    }

    fun close() {
        scope.cancel()
    }
}

