# Media Transcoding & Document Processing Pipeline 🎥📄

A high-concurrency, priority-scheduled background processing system built in **C# (.NET)**. Designed to solve resource starvation in media platforms by ensuring lightweight user uploads (like profile avatars and PDF resume thumbnails) process instantly while heavy CPU-bound tasks (like 4K video transcoding) are executed gracefully in the background.

---

## 🌟 Key Features

* **Smart Workload Prioritization:** Uses native .NET `PriorityQueue<TElement, TPriority>` to jump fast UI uploads ahead of long-running video encodes.
* **Corrupt File Isolation (DLQ):** Instantly quarantines unrecoverable media files (e.g., corrupt headers or invalid codecs) into a **Dead Letter Queue** without crashing worker threads or wasting CPU cycles on retries.
* **Transient Error Resilience:** Automatically catches worker timeouts (e.g., FFmpeg crashes) and applies exponential backoff retries.
* **Concurrent Worker Engine:** Coordinates multiple thread workers safely using `SemaphoreSlim` and `lock` primitives to maximize throughput without memory starvation.
* **Graceful Shutdown Support:** Utilizes `CancellationToken` signals to let active transcoding tasks finish cleanly.

---

## 🏗️ Pipeline Tier Structure

| Priority Tier | Job Type | Target Latency | Purpose |
| :--- | :--- | :--- | :--- |
| **Priority 1** | `AVATAR` | ~200 ms | Instant profile picture cropping & thumbnailing. |
| **Priority 2** | `PDF` | ~400 ms | Fast document page previews for user uploads. |
| **Priority 3** | `IMAGE` | ~800 ms | Batch gallery image compression. |
| **Priority 4** | `VIDEO_1080P` | ~1.5 sec | Standard resolution video encoding. |
| **Priority 5** | `VIDEO_4K` | Heavy CPU | Long-running 4K video rendering and chunking. |

---

## 📂 System Architecture

* **`MediaJob`**: Immutable record representing media metadata, file types, priorities, and retry budgets.
* **`BlockingPriorityQueue`**: Min-heap thread-safe queue coordinating async workers.
* **`DeadLetterQueue`**: Thread-safe quarantine store for corrupted files and failed job auditing.
* **`MediaJobProcessor`**: Concurrent worker thread performing media encoding and exception classification.
* **`MediaPipelineProducer`**: Entry-point API for publishing jobs into the pipeline.

---

## 🚀 Getting Started

### Prerequisites
* [.NET 8.0 SDK or higher](https://dotnet.microsoft.com/)

### Running the Application
1. Clone the repository and navigate into the folder:
   ```powershell
   git clone https://github.com/preshstoner/Media_Processing_Pipeline.git
   cd Media-Transcoding-Pipeline
   dotnet run
