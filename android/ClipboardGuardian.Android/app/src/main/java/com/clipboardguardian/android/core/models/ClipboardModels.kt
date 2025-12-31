package com.clipboardguardian.android.core.models

enum class RequestType { READ, WRITE }

data class PendingRequest(
    val id: String,
    val type: RequestType,
    val sample: String?
)

