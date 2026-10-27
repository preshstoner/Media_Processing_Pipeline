using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// 1. Media Processing Job Model
public record MediaJob(
    string JobId,
    string FileName,
    string FileType, // "AVATAR", "PDF", "IMAGE", "VIDEO_1080P", "VIDEO_4K"
    int Priority, // 1= Highest (Avater/Thumbnail), 5 = lowest (4K Transcode)
    int EstimatedTimeMs, // Simulated CPU work duration
    int RetryCount = 0,
    int MaxRetries = 2
);

//2. Thread-Safe Blocking Priority Queue
public class BlockingPriorityQueue
{
    private readonly PriorityQueue<MediaJob, int> _queue = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _signal = new(0);

    public void Enqueue(MediaJob job)
    {
        lock (_lock)
        {
            _queue.Enqueue(job, job.Priority);
            _signal.Release();
        }
    }

    public async Task<MediaJob> DequeueAsync(CancellationToken cancellationToken = default)
    {
        await _signal.WaitAsync(cancellationToken);

        lock (_lock)
        {
            return _queue.Dequeue(); 
        }
    }
}

// 3. Dead Letter Queue for Corrupt Files / Rendering Errors
public class DeadLetterQueue
{
    private readonly List<(MediaJob Job, string ErrorReason)> _quarantinedFiles = new();
    private readonly object _lock = new();

    public void Add(MediaJob job, string errorReason)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"🚨 [DLQ QUARANTINE] File '{job.FileName}' ({job.JobId}) failed permanently: {errorReason}");
            Console.ResetColor();
            _quarantinedFiles.Add((job, errorReason));
        }
    }

    public IReadOnlyList<(MediaJob Job, string ErrorReason)> GetAll()
    {
        lock (_lock)
        {
            return _quarantinedFiles.AsReadOnly();
        }
    }
}


// 4. Media Processing Worker
public class MediaJobProcessor(BlockingPriorityQueue jobQueue, DeadLetterQueue deadLetterQueue, int workerId)
{
    private readonly BlockingPriorityQueue _jobQueue = jobQueue;
    private readonly DeadLetterQueue _deadLetterQueue = deadLetterQueue;
    private readonly int _workerId = workerId;

    public async Task ProcessJobsAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var job = await _jobQueue.DequeueAsync(cancellationToken);
                if (job != null)
                {
                    await ProcessMediaAsync(job, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessMediaAsync(MediaJob job, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"⚙️ [Worker-{_workerId}] Processing [{job.FileType}] '{job.FileName}' | Priority: {job.Priority} | Est. Duration: {job.EstimatedTimeMs}ms");

            // Simulate CPU-heavy encoding / rendering work
            await Task.Delay(job.EstimatedTimeMs, ct);

            // Simulate random processing errors (e.g., corrupt codecs, invalid headers)
            var outcome = Random.Shared.Next(0, 6);

            if (outcome == 0)
            {
                // Unrecoverable corruption error( Fatal)
                throw new InvalidDataException("Corrupt media file header or unsupported codec.");
            }
            else if (outcome == 1)
            {
                // Temporary transcoding glitch (Transient)
                throw new TimeoutException("FFmeg worker process timed out.");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ SUCCESS [Worker-{_workerId}]: Completed '{job.FileName}' [{job.FileType}]");
            Console.ResetColor();
        }
        catch (InvalidDataException ex)
        {
            // Direct to DLQ without retries on corrupt media
            _deadLetterQueue.Add(job, ex.Message);
        }
        catch (Exception ex)
        {
            // Retry on transient processing timeouts
            await HandleTransientFailureAsync(job, ex);
        }
    }

    private async Task HandleTransientFailureAsync(MediaJob job, Exception ex)
    {
        if (job.RetryCount < job.MaxRetries)
        {
            var retryJob = job with { RetryCount = job.RetryCount + 1 };

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ Transient Failure on '{job.FileName}': {ex.Message}. Retrying ({retryJob.RetryCount}/{retryJob.MaxRetries})...");
            Console.ResetColor();

            await Task.Delay(500 * retryJob.RetryCount);
            _jobQueue.Enqueue(retryJob);
        }
        else
        {
            _deadLetterQueue.Add(job, $"Exceeded max rendering retries ({job.MaxRetries}). Reason: {ex.Message}");
        }
    }
}

// 5. Media Pipeline Publisher
public class MediaPipelineProducer(BlockingPriorityQueue jobQueue)
{
    private readonly BlockingPriorityQueue _jobQueue = jobQueue;

    public void UploadAvatar(string fileId, string fileName)
    {
        var job = new MediaJob(fileId, fileName, "AVATAR", Priority: 1, EstimatedTimeMs: 200);
        Console.WriteLine($"📸 Enqueued Avatar Upload: '{fileName}' (Priority 1)");
        _jobQueue.Enqueue(job);
    }

    public void UploadResume(string fileId, string fileName)
    {
        var job = new MediaJob(fileId, fileName, "PDF", Priority: 2, EstimatedTimeMs: 400);
        Console.WriteLine($"📄 Enqueued Resume Preview: '{fileName}' (Priority 2)");
        _jobQueue.Enqueue(job);
    }

    public void Upload4KVideo(string fileId, string fileName)
    {
        var job = new MediaJob(fileId, fileName, "VIDEO_4K", Priority: 5, EstimatedTimeMs: 1800);
        Console.WriteLine($"🎥 Enqueued 4K Video Transcode: '{fileName}' (Priority 5)");
        _jobQueue.Enqueue(job);
    }
}

// 6. System Execution Simulation
class Program
{
    static async Task Main(string[] args)
    {
        var queue = new BlockingPriorityQueue();
        var dlq = new DeadLetterQueue();
        var producer = new MediaPipelineProducer(queue);
        var cts = new CancellationTokenSource();

        // Spin up 2 concurrent transcoder workers
        var workers = new[]
        {
            Task.Run(() => new MediaJobProcessor(queue, dlq, workerId: 1).ProcessJobsAsync(cts.Token)),
            Task.Run(() => new MediaJobProcessor(queue, dlq, workerId: 2).ProcessJobsAsync(cts.Token))
        };

        Console.WriteLine("--- MEDIA & DOCUMENT PROCESSING PIPELINE SIMULATION ---\n");

        // 1. First, queue a heavy 4K Video Transcode (Priority 5)
        producer.Upload4KVideo("VID-001", "Wedding_Raw_4K.mov");

        // 2. A split second later, a user uploads an Avatar photo and a Resume PDF
        producer.UploadAvatar("IMG-001", "User_Profile_Pic.png");
        producer.UploadResume("DOC-001", "Software_Engineer_CV.pdf");

        // 3. Queue another heavy 4K transcode and a fast thumbnail
        producer.Upload4KVideo("VID-002", "Product_Demo_4k.mp4");
        producer.UploadAvatar("IMG-002", "Company_logo.png");

        // Let workers process for 6 seconds
        await Task.Delay(6000);

        // Clean shutdown
        cts.Cancel();
        await Task.WhenAll(workers);

        Console.WriteLine("\n--- PIPELINE SHUTDOWN COMPLETE ---");
        Console.WriteLine($"Total Quarantined Files in DLQ: {dlq.GetAll().Count}");
        foreach (var quarantined in dlq.GetAll())
        {
            Console.WriteLine($" -< Quarantied File: {quarantined.Job.FileName} | Reason: {quarantined.ErrorReason}");
        }
    }
}