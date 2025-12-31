package com.clipboardguardian.android

import android.content.ClipData
import android.content.ClipboardManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.clipboardguardian.android.databinding.ActivityMainBinding
import com.clipboardguardian.android.services.GuardianService
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

class MainActivity : AppCompatActivity() {
    private lateinit var binding: ActivityMainBinding
    private var pendingStart = false
    private lateinit var clipboardManager: ClipboardManager
    private var receiverRegistered = false
    private val historyTimeFormatter = DateTimeFormatter.ofPattern("HH:mm:ss")

    private val serviceStateReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent?.action != GuardianService.ACTION_STATE_CHANGED) return
            updateStatus()
        }
    }

    private val requestNotificationsPermission =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            if (pendingStart) {
                pendingStart = false
                if (granted) {
                    startProtection()
                } else {
                    updateStatus()
                }
            }
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        clipboardManager = getSystemService(ClipboardManager::class.java)

        binding.startButton.setOnClickListener {
            ensurePermissionsAndStart()
        }

        binding.stopButton.setOnClickListener {
            stopService(Intent(this, GuardianService::class.java))
            applyUiState(false)
        }

        binding.refreshHistoryButton.setOnClickListener {
            refreshHistory()
        }
    }

    override fun onStart() {
        super.onStart()
        if (!receiverRegistered) {
            receiverRegistered = true
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                registerReceiver(
                    serviceStateReceiver,
                    IntentFilter(GuardianService.ACTION_STATE_CHANGED),
                    RECEIVER_NOT_EXPORTED
                )
            } else {
                @Suppress("DEPRECATION")
                registerReceiver(serviceStateReceiver, IntentFilter(GuardianService.ACTION_STATE_CHANGED))
            }
        }
    }

    override fun onStop() {
        super.onStop()
        if (receiverRegistered) {
            receiverRegistered = false
            try {
                unregisterReceiver(serviceStateReceiver)
            } catch (_: IllegalArgumentException) {
            }
        }
    }

    override fun onResume() {
        super.onResume()
        updateStatus()
        refreshHistory()
    }

    private fun ensurePermissionsAndStart() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            val hasNotificationsPermission = ContextCompat.checkSelfPermission(
                this,
                android.Manifest.permission.POST_NOTIFICATIONS
            ) == PackageManager.PERMISSION_GRANTED

            if (!hasNotificationsPermission) {
                pendingStart = true
                requestNotificationsPermission.launch(android.Manifest.permission.POST_NOTIFICATIONS)
                return
            }
        }

        startProtection()
    }

    private fun startProtection() {
        try {
            ContextCompat.startForegroundService(this, Intent(this, GuardianService::class.java))
        } catch (_: SecurityException) {
        } catch (_: IllegalStateException) {
        }
        applyUiState(true)
    }

    private fun updateStatus() {
        val isRunning = GuardianService.readPersistedRunningState(this)
        val textRes = if (isRunning) {
            R.string.status_running
        } else {
            R.string.status_stopped
        }
        binding.statusText.setText(textRes)
        binding.startButton.isEnabled = !isRunning
        binding.stopButton.isEnabled = isRunning
    }

    private fun applyUiState(running: Boolean) {
        val textRes = if (running) R.string.status_running else R.string.status_stopped
        binding.statusText.setText(textRes)
        binding.startButton.isEnabled = !running
        binding.stopButton.isEnabled = running
    }

    private fun refreshHistory() {
        val emptyText = getString(R.string.history_empty)
        lifecycleScope.launch {
            val history = withContext(Dispatchers.IO) {
                readHistoryText(emptyText)
            }
            binding.historyText.text = history
        }
    }

    private fun readHistoryText(emptyText: String): String {
        val logFile = File(filesDir, "logs/clipboard_log.ndjson")
        if (!logFile.exists()) return emptyText

        val entries = ArrayDeque<String>()
        logFile.forEachLine { line ->
            if (line.isBlank()) return@forEachLine
            try {
                val json = JSONObject(line)
                val decision = json.optString("decision")
                if (decision != "allowed" && decision != "blocked") return@forEachLine

                val action = json.optString("action")
                if (action != "copy" && action != "paste") return@forEachLine
                val actionLabel = if (action == "copy") "копирование" else "вставка"

                val time = formatTimestamp(json.optString("timestamp"))
                val sample = normalizeSample(json.optString("sample"))
                val decisionLabel = if (decision == "allowed") "разрешил" else "запретил"
                val displaySample = if (sample.isBlank()) "Пусто" else sample

                entries.addLast("$time $displaySample $actionLabel ответ - $decisionLabel")
                if (entries.size > 200) entries.removeFirst()
            } catch (_: Throwable) {
            }
        }

        if (entries.isEmpty()) return emptyText
        return entries.joinToString("\n")
    }

    private fun formatTimestamp(raw: String?): String {
        if (raw.isNullOrBlank()) return "--:--:--"
        return try {
            val instant = Instant.parse(raw)
            historyTimeFormatter.format(instant.atZone(ZoneId.systemDefault()))
        } catch (_: Throwable) {
            "--:--:--"
        }
    }

    private fun normalizeSample(sample: String?, maxLength: Int = 160): String {
        if (sample.isNullOrBlank()) return ""
        val normalized = sample.replace("\r", " ").replace("\n", " ").trim()
        return if (normalized.length > maxLength) {
            normalized.substring(0, maxLength) + "…"
        } else {
            normalized
        }
    }
}
