using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ZHmaster
{
    sealed class AudioInfo
    {
        internal string Codec, SampleFormat, Layout;
        internal int SampleRate, Channels, Bits;
        internal double Duration;
        internal string DurationText
        {
            get
            {
                if (Duration <= 0 || Double.IsNaN(Duration) || Double.IsInfinity(Duration) || Duration >= Int64.MaxValue - 1) return "--:--";
                long seconds = (long)Math.Round(Duration, MidpointRounding.AwayFromZero);
                return seconds >= 3600 ? (seconds / 3600).ToString(CultureInfo.InvariantCulture) + ":" + ((seconds / 60) % 60).ToString("D2") + ":" + (seconds % 60).ToString("D2") : (seconds / 60).ToString("D2") + ":" + (seconds % 60).ToString("D2");
            }
        }
        internal string Description
        {
            get
            {
                bool floating = SampleFormat.StartsWith("flt", StringComparison.Ordinal) || SampleFormat.StartsWith("dbl", StringComparison.Ordinal);
                return (SampleRate / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " kHz · " + Bits +
                    (floating ? " 位浮点 · " : " 位 · ") + Channels + " 声道";
            }
        }
    }

    sealed class AudioResult
    {
        internal string OutputPath, PcmHash;
        internal AudioInfo Source, Output;
    }

    sealed class AudioExporter
    {
        readonly string ffmpeg, ffprobe;
        const string Common = "-hide_banner -nostdin -loglevel error -xerror -err_detect explode -nostats -progress pipe:1 -stats_period 0.25 -protocol_whitelist file,pipe ";
        internal static bool Available { get { return FindDirectory() != null; } }

        static string FindDirectory()
        {
            string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg");
            List<string> candidates = new List<string> { bundled };
            candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            foreach (string item in candidates)
            {
                string path = item.Trim().Trim('"');
                if (path.Length > 0 && File.Exists(Path.Combine(path, "ffmpeg.exe")) && File.Exists(Path.Combine(path, "ffprobe.exe"))) return path;
            }
            return null;
        }
        internal AudioExporter()
        {
            string directory = FindDirectory();
            if (directory == null) throw new InvalidOperationException("缺少音频转换组件，请检查程序目录中的 tools\\ffmpeg 文件夹。");
            ffmpeg = Path.Combine(directory, "ffmpeg.exe"); ffprobe = Path.Combine(directory, "ffprobe.exe");
        }

        static string Quote(string value)
        {
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') result.Append('\\', slashes * 2 + 1).Append('"');
                else result.Append('\\', slashes).Append(character);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        static void CheckCancelled(Func<bool> cancelled)
        {
            if (cancelled != null && cancelled()) throw new OperationCanceledException();
        }
        static void AppendBounded(StringBuilder builder, string line)
        {
            builder.AppendLine(line);
            if (builder.Length > 1024 * 1024) builder.Remove(0, builder.Length - 1024 * 1024);
        }
        static string Run(string executable, string arguments, Func<bool> cancelled, Action<string> onLine, int timeoutMilliseconds)
        {
            CheckCancelled(cancelled);
            StringBuilder output = new StringBuilder(), errors = new StringBuilder();
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, arguments) {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                    if (e.Data == null) return;
                    lock (output) AppendBounded(output, e.Data);
                    if (onLine != null) onLine(e.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                    if (e.Data != null) lock (errors) AppendBounded(errors, e.Data);
                };
                process.Start();
                try
                {
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    Stopwatch elapsed = Stopwatch.StartNew();
                    while (!process.WaitForExit(100))
                    {
                        CheckCancelled(cancelled);
                        if (timeoutMilliseconds > 0 && elapsed.ElapsedMilliseconds > timeoutMilliseconds)
                            throw new IOException("读取音频参数超时，请检查文件是否可正常打开。");
                    }
                    process.WaitForExit();
                    CheckCancelled(cancelled);
                    if (process.ExitCode != 0)
                    {
                        string message;
                        lock (errors) message = errors.ToString().Trim();
                        if (message.Length > 3000) message = message.Substring(message.Length - 3000);
                        throw new IOException(String.IsNullOrWhiteSpace(message) ? "音频转换组件未能处理此文件。" : message);
                    }
                    lock (output) return output.ToString();
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                }
            }
        }

        static string Value(Dictionary<string, object> data, string key)
        {
            object value; return data.TryGetValue(key, out value) && value != null ? System.Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }
        static int Integer(Dictionary<string, object> data, string key)
        {
            int value; return Int32.TryParse(Value(data, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }
        static double Seconds(string text)
        {
            double value; return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !Double.IsInfinity(value) && !Double.IsNaN(value) && value > 0 ? value : 0;
        }
        internal AudioInfo Probe(string input, Func<bool> cancelled)
        {
            string arguments = "-v error -protocol_whitelist file,pipe -select_streams a:0 -show_entries " +
                "stream=codec_name,sample_fmt,bits_per_sample,bits_per_raw_sample,sample_rate,channels,channel_layout,duration:format=duration -of json -i " + Quote(input);
            string json = Run(ffprobe, arguments, cancelled, null, 30000);
            Dictionary<string, object> data = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            object streams;
            if (data == null || !data.TryGetValue("streams", out streams)) throw new IOException("文件中没有可读取的音轨。");
            object[] array = streams as object[];
            if (array == null || array.Length == 0) throw new IOException("文件中没有可读取的音轨。");
            Dictionary<string, object> stream = array[0] as Dictionary<string, object>;
            if (stream == null) throw new IOException("无法读取音频参数。");
            AudioInfo info = new AudioInfo {
                Codec = Value(stream, "codec_name"), SampleFormat = Value(stream, "sample_fmt"),
                SampleRate = Integer(stream, "sample_rate"), Channels = Integer(stream, "channels"),
                Bits = Integer(stream, "bits_per_raw_sample"), Layout = Value(stream, "channel_layout"),
                Duration = Seconds(Value(stream, "duration"))
            };
            if (info.Bits == 0) info.Bits = Integer(stream, "bits_per_sample");
            if (info.Bits == 0)
            {
                string sample = info.SampleFormat.TrimEnd('p');
                info.Bits = sample == "u8" ? 8 : sample == "s16" ? 16 : sample == "s32" || sample == "flt" ? 32 : sample == "s64" || sample == "dbl" ? 64 : 0;
            }
            object format;
            if (info.Duration <= 0 && data.TryGetValue("format", out format))
            {
                Dictionary<string, object> container = format as Dictionary<string, object>;
                if (container != null) info.Duration = Seconds(Value(container, "duration"));
            }
            if (info.SampleRate <= 0 || info.Channels <= 0 || info.Bits <= 0) throw new IOException("无法确定采样率、声道或精度，已停止转换以避免音质损失。");
            return info;
        }
        internal static string OutputCodec(AudioInfo source)
        {
            switch (source.SampleFormat.TrimEnd('p'))
            {
                case "u8": return "pcm_u8";
                case "s16": return "pcm_s16le";
                case "s32": return source.Bits <= 24 ? "pcm_s24le" : "pcm_s32le";
                case "s64": return "pcm_s64le";
                case "flt": return "pcm_f32le";
                case "dbl": return "pcm_f64le";
                default: throw new IOException("不支持无损保留这种解码精度：" + source.SampleFormat + "。未生成降质文件。");
            }
        }
        static string CanonicalCodec(AudioInfo source)
        {
            string format = source.SampleFormat.TrimEnd('p');
            return format == "flt" ? "pcm_f32le" : format == "dbl" ? "pcm_f64le" : format == "s64" ? "pcm_s64le" : "pcm_s32le";
        }
        static Action<string> Progress(AudioInfo source, double lower, double upper, string phase, Action<double, string> report)
        {
            return delegate(string line) {
                if (report == null) return;
                long micros;
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && Int64.TryParse(line.Substring(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out micros) && source.Duration > 0)
                    report(lower + Math.Max(0, Math.Min(1, micros / 1000000.0 / source.Duration)) * (upper - lower), phase);
                else if (line == "progress=end") report(upper, phase);
            };
        }
        string Hash(string input, AudioInfo source, string codec, double low, double high, string phase, Func<bool> cancelled, Action<double, string> report)
        {
            string arguments = Common + "-i " + Quote(input) + " -map 0:a:0 -vn -sn -dn -c:a " + codec + " -f hash -hash sha256 -";
            string output = Run(ffmpeg, arguments, cancelled, Progress(source, low, high, phase, report), 0);
            Match match = Regex.Match(output, @"(?im)^SHA256=([a-f0-9]{64})\s*$");
            if (!match.Success) throw new IOException("未获得完整的音频校验值，已停止保存。");
            return match.Groups[1].Value.ToLowerInvariant();
        }
        static string AvailableOutput(string directory, string source)
        {
            string stem = Path.GetFileNameWithoutExtension(source);
            string path = Path.Combine(directory, stem + ".wav");
            for (int number = 2; File.Exists(path); number++) path = Path.Combine(directory, stem + " (" + number + ").wav");
            return path;
        }
        internal AudioResult Convert(string source, string directory, Func<bool> cancelled, Action<double, string> report, Action<AudioInfo> detected)
        {
            source = Path.GetFullPath(source); directory = Path.GetFullPath(directory);
            if (!File.Exists(source)) throw new FileNotFoundException("源音频不存在。", source);
            CheckCancelled(cancelled);
            if (report != null) report(0, "读取音频参数");
            AudioInfo info = Probe(source, cancelled);
            string codec = OutputCodec(info), canonical = CanonicalCodec(info);
            if (detected != null) detected(info);
            Directory.CreateDirectory(directory);
            string output = AvailableOutput(directory, source);
            string temporary = Path.Combine(directory, ".zhmaster-audio-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                if (report != null) report(0.02, "转换中");
                string arguments = Common + "-n -i " + Quote(source) + " -map 0:a:0 -vn -sn -dn -map_metadata 0 -c:a " + codec + " -rf64 auto -f wav " + Quote(temporary);
                Run(ffmpeg, arguments, cancelled, Progress(info, 0.02, 0.60, "转换中", report), 0);
                if (report != null) report(0.60, "校验源音频");
                string sourceHash = Hash(source, info, canonical, 0.60, 0.79, "校验源音频", cancelled, report);
                if (report != null) report(0.79, "校验 WAV");
                string outputHash = Hash(temporary, info, canonical, 0.79, 0.98, "校验 WAV", cancelled, report);
                if (sourceHash != outputHash) throw new IOException("解码后的音频数据不一致，已删除未通过校验的 WAV。");
                AudioInfo saved = Probe(temporary, cancelled);
                if (saved.Codec != codec || saved.SampleRate != info.SampleRate || saved.Channels != info.Channels ||
                    (!String.IsNullOrEmpty(info.Layout) && !String.IsNullOrEmpty(saved.Layout) && info.Layout != saved.Layout))
                    throw new IOException("WAV 音频参数与源文件不一致，已停止保存。");
                using (FileStream input = File.OpenRead(temporary))
                {
                    byte[] header = new byte[12];
                    if (input.Read(header, 0, 12) != 12 || Encoding.ASCII.GetString(header, 8, 4) != "WAVE" ||
                        (Encoding.ASCII.GetString(header, 0, 4) != "RIFF" && Encoding.ASCII.GetString(header, 0, 4) != "RF64"))
                        throw new IOException("输出不是有效的 WAV 文件。");
                }
                CheckCancelled(cancelled);
                File.Move(temporary, output);
                if (report != null) report(1, "已完成");
                return new AudioResult { OutputPath = output, PcmHash = sourceHash, Source = info, Output = saved };
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
