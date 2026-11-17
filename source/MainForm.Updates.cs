using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ZHmaster
{
    sealed partial class MainForm
    {
        sealed class AudioUpdate
        {
            internal AudioInfo Info;
            internal string Error;
        }
        readonly object updateGate = new object();
        readonly Dictionary<string, string> pendingStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AudioUpdate> pendingAudio = new Dictionary<string, AudioUpdate>(StringComparer.OrdinalIgnoreCase);
        readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer { Interval = 50 };
        bool updateWakeScheduled, progressPending;
        int pendingProgress;
        string pendingStatus;

        void InitializeUpdateTimers()
        {
            resizeTimer.Tick += delegate {
                if (!resizePending) { resizeTimer.Stop(); return; }
                ApplyPendingResize();
            };
            updateTimer.Tick += delegate { FlushUiUpdates(false); };
        }
        void WakeUpdateTimer()
        {
            // Keep one outstanding UI wake-up, regardless of how fast a worker reports.
            bool wake;
            lock (updateGate) { wake = !updateWakeScheduled; updateWakeScheduled = true; }
            if (wake) Ui(delegate { updateTimer.Start(); });
        }
        void QueueProgress(int value, string text)
        {
            if (shuttingDown) return;
            lock (updateGate) { pendingProgress = value; pendingStatus = text; progressPending = true; }
            WakeUpdateTimer();
        }
        void QueueItemState(string path, string state)
        {
            if (shuttingDown) return;
            lock (updateGate) pendingStates[path] = state;
            WakeUpdateTimer();
        }
        void QueueAudioMetadata(string path, AudioInfo info, string error)
        {
            if (shuttingDown) return;
            lock (updateGate) pendingAudio[path] = new AudioUpdate { Info = info, Error = error };
            WakeUpdateTimer();
        }
        void FlushUiUpdates(bool force)
        {
            if (shuttingDown || interactiveResize && !force) return;
            KeyValuePair<string, string>[] states;
            KeyValuePair<string, AudioUpdate>[] audio;
            bool showProgress;
            int value;
            string text;
            lock (updateGate)
            {
                int limit = force ? Int32.MaxValue : 128;
                states = pendingStates.Take(limit).ToArray();
                audio = pendingAudio.Take(limit).ToArray();
                foreach (var pair in states) pendingStates.Remove(pair.Key);
                foreach (var pair in audio) pendingAudio.Remove(pair.Key);
                showProgress = progressPending; value = pendingProgress; text = pendingStatus; progressPending = false;
                if (pendingStates.Count == 0 && pendingAudio.Count == 0)
                {
                    updateTimer.Stop(); updateWakeScheduled = false;
                }
            }
            if (states.Length > 0 || audio.Length > 0)
            {
                list.BeginUpdate();
                try
                {
                    foreach (var pair in states) if (itemStates.ContainsKey(pair.Key)) SetItemState(pair.Key, pair.Value);
                    foreach (var pair in audio)
                    {
                        if (!queuePaths[2].Contains(pair.Key)) continue;
                        if (pair.Value.Info != null) SetAudioMetadata(pair.Key, pair.Value.Info);
                        else if (!audioMetadata.ContainsKey(pair.Key))
                        {
                            audioParameters[pair.Key] = pair.Value.Error;
                            if (mode == 2) UpdateAudioRow(pair.Key);
                        }
                    }
                }
                finally { list.EndUpdate(); }
            }
            if (showProgress && busy)
            {
                if (progress.Value != value) progress.Value = value;
                if (!cancel && status.Text != text) status.Text = text;
            }
        }
    }
}
