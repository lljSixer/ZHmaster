using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ZHmaster
{
    sealed partial class MainForm
    {
        readonly Dictionary<string, AudioInfo> audioMetadata = new Dictionary<string, AudioInfo>(StringComparer.OrdinalIgnoreCase);
        readonly object previewGate = new object();
        readonly Queue<string> previewQueue = new Queue<string>();
        readonly HashSet<string> previewPending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Thread previewThread;
        bool previewRunning;
        volatile bool shuttingDown;

        void ScheduleAudioPreview(string path)
        {
            if (!IsHandleCreated || !audioEnvironmentAvailable || shuttingDown || audioMetadata.ContainsKey(path)) return;
            lock (previewGate)
            {
                if (!previewPending.Add(path)) return;
                previewQueue.Enqueue(path);
                if (previewRunning) return;
                previewRunning = true;
                previewThread = new Thread(ReadAudioPreviews) { IsBackground = true, Name = "ZHmaster audio metadata" };
                previewThread.Start();
            }
        }
        void ReadAudioPreviews()
        {
            try
            {
                AudioExporter exporter = new AudioExporter();
                while (!shuttingDown)
                {
                    if (busy || interactiveResize) { Thread.Sleep(50); continue; }
                    string path;
                    lock (previewGate)
                    {
                        if (previewQueue.Count == 0) return;
                        path = previewQueue.Dequeue();
                    }
                    try
                    {
                        AudioInfo info = exporter.Probe(path, delegate { return shuttingDown; });
                        QueueAudioMetadata(path, info, null);
                    }
                    catch (OperationCanceledException) { if (shuttingDown) return; }
                    catch (Exception)
                    {
                        QueueAudioMetadata(path, null, "无法读取，转换时重试");
                    }
                    finally { lock (previewGate) previewPending.Remove(path); }
                }
            }
            catch (Exception)
            {
                lock (previewGate) { previewQueue.Clear(); previewPending.Clear(); }
                Ui(delegate {
                    foreach (string path in queues[2]) if (!audioMetadata.ContainsKey(path))
                    {
                        audioParameters[path] = "读取失败，可直接开始转换";
                        if (mode == 2) UpdateAudioRow(path);
                    }
                });
            }
            finally
            {
                lock (previewGate)
                {
                    previewRunning = false;
                    if (!shuttingDown && previewQueue.Count > 0)
                    {
                        previewRunning = true;
                        previewThread = new Thread(ReadAudioPreviews) { IsBackground = true, Name = "ZHmaster audio metadata" };
                        previewThread.Start();
                    }
                }
            }
        }
        void SetAudioMetadata(string path, AudioInfo info)
        {
            if (!queuePaths[2].Contains(path)) return;
            audioMetadata[path] = info; audioParameters[path] = info.Description;
            if (mode == 2) UpdateAudioRow(path);
        }
        void UpdateAudioRow(string path)
        {
            AudioInfo info; string parameters;
            bool known = audioMetadata.TryGetValue(path, out info);
            ListViewItem item;
            if (!visibleRows.TryGetValue(path, out item) || item.SubItems.Count != 4) return;
            string duration = known ? info.DurationText : "--:--";
            string description = audioParameters.TryGetValue(path, out parameters) ? parameters : "读取中…";
            if (item.SubItems[2].Text != duration) item.SubItems[2].Text = duration;
            if (item.SubItems[3].Text != description) item.SubItems[3].Text = description;
            item.ToolTipText = path + (known ? "\n时长：" + info.DurationText + "\n" + info.Description : "");
        }

        void StopAudioPreviews()
        {
            shuttingDown = true;
            Thread worker;
            lock (previewGate) { previewQueue.Clear(); worker = previewThread; }
            if (worker != null && worker.IsAlive && worker != Thread.CurrentThread) worker.Join(750);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { StopAudioPreviews(); resizeTimer.Dispose(); updateTimer.Dispose(); tips.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
