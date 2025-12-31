package com.clipboardguardian.android

import android.content.Intent
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import com.clipboardguardian.android.databinding.ActivityApprovalBinding
import com.clipboardguardian.android.services.GuardianService
import com.clipboardguardian.android.hooks.XposedContract

class ApprovalActivity : AppCompatActivity() {

    private lateinit var binding: ActivityApprovalBinding
    private var currentRequestId: String? = null
    private var currentTargetPackage: String? = null
    private var currentMode: String = MODE_WRITE

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityApprovalBinding.inflate(layoutInflater)
        setContentView(binding.root)

        renderFromIntent(intent)

        binding.allowButton.setOnClickListener {
            val requestId = currentRequestId ?: return@setOnClickListener
            publishDecision(GuardianService.DECISION_ALLOW, requestId, currentTargetPackage, currentMode)
        }

        binding.denyButton.setOnClickListener {
            val requestId = currentRequestId ?: return@setOnClickListener
            publishDecision(GuardianService.DECISION_DENY, requestId, currentTargetPackage, currentMode)
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        renderFromIntent(intent)
    }

    private fun renderFromIntent(intent: Intent) {
        val mode = intent.getStringExtra(EXTRA_MODE) ?: MODE_WRITE
        val sample = intent.getStringExtra(EXTRA_SAMPLE)
        val requestId = intent.getStringExtra(GuardianService.EXTRA_REQUEST_ID)
        val targetPackage = intent.getStringExtra(EXTRA_TARGET_PACKAGE)
        val appLabel = intent.getStringExtra(EXTRA_APP_LABEL)
        if (requestId == null) {
            finish()
            return
        }

        currentRequestId = requestId
        currentTargetPackage = targetPackage
        currentMode = mode

        when (mode) {
            MODE_READ -> {
                binding.titleText.text = getString(R.string.approval_title_read)
                binding.messageText.text = if (!appLabel.isNullOrBlank()) {
                    getString(R.string.approval_message_read_with_app, appLabel)
                } else {
                    getString(R.string.approval_message_read)
                }
            }
            else -> {
                binding.titleText.text = getString(R.string.approval_title_copy)
                binding.messageText.text = if (!appLabel.isNullOrBlank()) {
                    getString(R.string.approval_message_copy_with_app, appLabel)
                } else {
                    getString(R.string.approval_message_copy)
                }
            }
        }

        binding.sampleText.text = if (sample.isNullOrBlank()) {
            getString(R.string.approval_sample_placeholder)
        } else {
            sample
        }
    }

    private fun publishDecision(decision: String, requestId: String, targetPackage: String?, mode: String) {
        val action = if (targetPackage.isNullOrBlank()) {
            GuardianService.ACTION_DECISION
        } else {
            XposedContract.ACTION_DECISION_RESULT
        }
        val intent = Intent(action).apply {
            if (!targetPackage.isNullOrBlank()) {
                setPackage(targetPackage)
                putExtra(XposedContract.EXTRA_MODE, mode)
            } else {
                setPackage(packageName)
            }
            putExtra(GuardianService.EXTRA_DECISION, decision)
            putExtra(GuardianService.EXTRA_REQUEST_ID, requestId)
        }
        sendBroadcast(intent)
        finish()
    }

    companion object {
        private const val EXTRA_SAMPLE = "extra_sample"
        private const val EXTRA_MODE = "extra_mode"
        private const val EXTRA_TARGET_PACKAGE = "extra_target_package"
        private const val EXTRA_APP_LABEL = "extra_app_label"

        const val MODE_READ = "read"
        const val MODE_WRITE = "write"

        fun newIntent(context: android.content.Context, requestId: String, sample: String?): Intent {
            return Intent(context, ApprovalActivity::class.java).apply {
                putExtra(EXTRA_SAMPLE, sample)
                putExtra(GuardianService.EXTRA_REQUEST_ID, requestId)
                putExtra(EXTRA_MODE, MODE_WRITE)
            }
        }

        fun newReadIntent(context: android.content.Context, requestId: String, sample: String?): Intent {
            return Intent(context, ApprovalActivity::class.java).apply {
                putExtra(EXTRA_SAMPLE, sample)
                putExtra(GuardianService.EXTRA_REQUEST_ID, requestId)
                putExtra(EXTRA_MODE, MODE_READ)
            }
        }

        fun newXposedIntent(
            context: android.content.Context,
            requestId: String,
            mode: String,
            targetPackage: String,
            appLabel: String?,
            sample: String?
        ): Intent {
            return Intent(context, ApprovalActivity::class.java).apply {
                putExtra(EXTRA_SAMPLE, sample)
                putExtra(GuardianService.EXTRA_REQUEST_ID, requestId)
                putExtra(EXTRA_MODE, mode)
                putExtra(EXTRA_TARGET_PACKAGE, targetPackage)
                putExtra(EXTRA_APP_LABEL, appLabel)
            }
        }
    }
}
