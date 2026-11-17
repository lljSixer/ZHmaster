using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("ZHmaster 文件转换工作台")]
[assembly: System.Reflection.AssemblyVersion("1.2.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.2.0")]

namespace ZHmaster
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length > 0 && args[0] == "--images")
                {
                    if (args.Length < 3) throw new ArgumentException("--images output.pdf image1 [image2 ...]");
                    PdfImages.Convert(args.Skip(2).ToArray(), args[1], false, null, null);
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--images-a4")
                {
                    if (args.Length < 3) throw new ArgumentException("--images-a4 output.pdf image1 [image2 ...]");
                    PdfImages.Convert(args.Skip(2).ToArray(), args[1], true, null, null);
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--word")
                {
                    if (args.Length < 3) throw new ArgumentException("--word output-directory document1 [document2 ...]");
                    Directory.CreateDirectory(args[1]);
                    using (WordExporter word = new WordExporter())
                        foreach (string source in args.Skip(2)) word.Convert(source, args[1]);
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--audio")
                {
                    if (args.Length < 3) throw new ArgumentException("--audio output-directory source1 [source2 ...]");
                    AudioExporter audio = new AudioExporter();
                    foreach (string source in args.Skip(2)) audio.Convert(source, args[1], null, null, null);
                    return 0;
                }
                MainForm form = new MainForm();
                if (args.Length > 0) form.AddFiles(args);
                Application.Run(form);
                return 0;
            }
            catch (Exception error)
            {
                if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-error.txt"), error.ToString(), Encoding.UTF8);
                else MessageBox.Show(error.Message, "ZHmaster", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    static class Files
    {
        internal static readonly string[] Images = { ".jpg", ".jpeg", ".png", ".bmp" };
        internal static readonly string[] Documents = { ".doc", ".docx" };
        internal static readonly string[] Audio = { ".flac", ".ogg", ".opus", ".ape", ".wv", ".wav" };
        internal static string Available(string folder, string name)
        {
            string stem = Path.GetFileNameWithoutExtension(name);
            string result = Path.Combine(folder, stem + ".pdf");
            for (int n = 2; File.Exists(result); n++) result = Path.Combine(folder, stem + " (" + n + ").pdf");
            return result;
        }
        internal static void CheckPdf(string path)
        {
            using (FileStream input = File.OpenRead(path))
            {
                byte[] header = new byte[5];
                if (input.Read(header, 0, 5) != 5 || Encoding.ASCII.GetString(header) != "%PDF-")
                    throw new IOException("未生成有效的 PDF，请检查源文件后重试。");
            }
        }
        internal static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    sealed class PdfSink : IDisposable
    {
        readonly FileStream stream;
        readonly List<long> offsets = new List<long> { 0 };
        internal PdfSink(string path)
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            Text("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
        }
        internal void Text(string text)
        {
            byte[] bytes = Encoding.GetEncoding(28591).GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
        internal void Object(int number, string body)
        {
            while (offsets.Count <= number) offsets.Add(0);
            offsets[number] = stream.Position;
            Text(number + " 0 obj\n" + body + "\nendobj\n");
        }
        internal void Stream(int number, string dictionary, byte[] data)
        {
            while (offsets.Count <= number) offsets.Add(0);
            offsets[number] = stream.Position;
            Text(number + " 0 obj\n<< " + dictionary + " /Length " + data.Length + " >>\nstream\n");
            stream.Write(data, 0, data.Length);
            Text("\nendstream\nendobj\n");
        }
        internal void Complete()
        {
            long start = stream.Position;
            Text("xref\n0 " + offsets.Count + "\n0000000000 65535 f \n");
            for (int i = 1; i < offsets.Count; i++)
                Text(offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
            Text("trailer\n<< /Size " + offsets.Count + " /Root 1 0 R >>\nstartxref\n" + start + "\n%%EOF\n");
        }
        public void Dispose() { stream.Dispose(); }
    }

    static class PdfImages
    {
        static string N(double number) { return number.ToString("0.#####", CultureInfo.InvariantCulture); }
        static byte[] Deflate(byte[] raw)
        {
            using (MemoryStream result = new MemoryStream())
            {
                result.WriteByte(0x78); result.WriteByte(0x9c);
                using (DeflateStream zip = new DeflateStream(result, CompressionMode.Compress, true)) zip.Write(raw, 0, raw.Length);
                uint a = 1, b = 0;
                foreach (byte value in raw) { a = (a + value) % 65521; b = (b + a) % 65521; }
                uint checksum = (b << 16) | a;
                for (int shift = 24; shift >= 0; shift -= 8) result.WriteByte((byte)(checksum >> shift));
                return result.ToArray();
            }
        }
        static byte[] Rgb(Image image)
        {
            using (Bitmap bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
            {
                using (Graphics draw = Graphics.FromImage(bitmap))
                {
                    draw.Clear(Color.White);
                    draw.DrawImage(image, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                }
                BitmapData bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    byte[] data = new byte[checked(bitmap.Width * bitmap.Height * 3)];
                    byte[] row = new byte[bitmap.Width * 3];
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), row, 0, row.Length);
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            int at = y * row.Length + x * 3;
                            data[at] = row[x * 3 + 2]; data[at + 1] = row[x * 3 + 1]; data[at + 2] = row[x * 3];
                        }
                    }
                    return Deflate(data);
                }
                finally { bitmap.UnlockBits(bits); }
            }
        }
        static int JpegComponents(byte[] bytes)
        {
            int at = 2;
            while (at + 8 < bytes.Length)
            {
                if (bytes[at++] != 255) continue;
                while (at < bytes.Length && bytes[at] == 255) at++;
                if (at >= bytes.Length) break;
                int marker = bytes[at++];
                if (marker == 0xd9 || marker == 0xda) break;
                if (marker == 0x01 || marker >= 0xd0 && marker <= 0xd7) continue;
                if (at + 1 >= bytes.Length) break;
                int length = (bytes[at] << 8) | bytes[at + 1];
                if (length < 2 || at + length > bytes.Length) break;
                if ((marker >= 0xc0 && marker <= 0xc3) || (marker >= 0xc5 && marker <= 0xc7) || (marker >= 0xc9 && marker <= 0xcb) || (marker >= 0xcd && marker <= 0xcf))
                    return bytes[at + 7];
                at += length;
            }
            return 0;
        }
        internal static void Convert(string[] inputs, string output, bool a4, Action<int, string> progress, Func<bool> cancelled)
        {
            if (inputs.Length == 0) throw new ArgumentException("请先添加图片。");
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            if (File.Exists(output)) throw new IOException("输出文件已存在，请更换文件名。");
            string temporary = Path.Combine(Path.GetDirectoryName(output), ".zhmaster-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (PdfSink pdf = new PdfSink(temporary))
                {
                    pdf.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
                    string kids = String.Join(" ", Enumerable.Range(0, inputs.Length).Select(i => (3 + i * 3) + " 0 R"));
                    pdf.Object(2, "<< /Type /Pages /Count " + inputs.Length + " /Kids [" + kids + "] >>");
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        if (progress != null) progress(i, "正在处理：" + Path.GetFileName(inputs[i]));
                        try
                        {
                            using (Image image = Image.FromFile(inputs[i]))
                            {
                                if ((long)image.Width * image.Height > 100000000) throw new IOException("图片超过 1 亿像素，请缩小后再添加。");
                                int orientation = 1;
                                if (image.PropertyIdList.Contains(274)) orientation = image.GetPropertyItem(274).Value[0];
                                RotateFlipType[] rotate = { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone,
                                    RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone, RotateFlipType.Rotate180FlipX,
                                    RotateFlipType.Rotate90FlipX, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };
                                if (orientation > 1 && orientation < rotate.Length) image.RotateFlip(rotate[orientation]);
                                byte[] data = null;
                                string filter = "/FlateDecode", space = "/DeviceRGB";
                                string extension = Path.GetExtension(inputs[i]).ToLowerInvariant();
                                if ((extension == ".jpg" || extension == ".jpeg") && orientation == 1)
                                {
                                    byte[] jpeg = File.ReadAllBytes(inputs[i]);
                                    int components = JpegComponents(jpeg);
                                    if (components == 1 || components == 3)
                                    { data = jpeg; filter = "/DCTDecode"; space = components == 1 ? "/DeviceGray" : "/DeviceRGB"; }
                                }
                                if (data == null) data = Rgb(image);
                                double pw = 595.27559, ph = a4 ? 841.88976 : pw * image.Height / image.Width;
                                if (ph > 14000) { pw *= 14000 / ph; ph = 14000; }
                                double margin = a4 ? 18 : 0;
                                double scale = Math.Min((pw - margin * 2) / image.Width, (ph - margin * 2) / image.Height);
                                double w = image.Width * scale, h = image.Height * scale;
                                int page = 3 + i * 3, picture = page + 1, content = page + 2;
                                pdf.Object(page, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(pw) + " " + N(ph) + "] /Resources << /XObject << /Im " + picture + " 0 R >> >> /Contents " + content + " 0 R >>");
                                pdf.Stream(picture, "/Type /XObject /Subtype /Image /Width " + image.Width + " /Height " + image.Height + " /ColorSpace " + space + " /BitsPerComponent 8 /Filter " + filter, data);
                                string drawing = "q\n" + N(w) + " 0 0 " + N(h) + " " + N((pw - w) / 2) + " " + N((ph - h) / 2) + " cm\n/Im Do\nQ\n";
                                pdf.Stream(content, "", Encoding.ASCII.GetBytes(drawing));
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error) { throw new IOException(Path.GetFileName(inputs[i]) + "：" + error.Message, error); }
                    }
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    pdf.Complete();
                }
                Files.CheckPdf(temporary);
                File.Move(temporary, output);
                if (progress != null) progress(inputs.Length, "图片合并完成");
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    sealed class WordExporter : IDisposable
    {
        object application;
        object documents;
        int security = -1;
        static readonly string[] EngineIds = { "Word.Application", "KWPS.Application" };
        internal static bool Available
        {
            get { return EngineIds.Any(id => Type.GetTypeFromProgID(id) != null); }
        }
        internal WordExporter()
        {
            List<string> errors = new List<string>();
            foreach (string id in EngineIds)
            {
                Type type = Type.GetTypeFromProgID(id);
                if (type == null) continue;
                try
                {
                    application = Activator.CreateInstance(type);
                    dynamic word = application;
                    word.Visible = false;
                    word.DisplayAlerts = 0;
                    security = (int)word.AutomationSecurity;
                    word.AutomationSecurity = 3;
                    word.ScreenUpdating = false;
                    documents = word.Documents;
                    return;
                }
                catch (Exception error)
                {
                    errors.Add(id + "：" + error.Message);
                    Dispose();
                }
            }
            if (errors.Count == 0)
                throw new InvalidOperationException("未检测到可用的文档转换环境。请安装并初始化 Microsoft Word，或提供兼容自动化接口的 WPS 桌面版。");
            throw new InvalidOperationException("无法启动文档转换环境，请先手动打开办公软件，完成初始化后重试。\n" + String.Join("\n", errors));
        }
        internal string Convert(string source, string directory)
        {
            source = Path.GetFullPath(source);
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            string output = Files.Available(directory, Path.GetFileNameWithoutExtension(source) + ".pdf");
            string temporary = Path.Combine(directory, ".zhmaster-" + Guid.NewGuid().ToString("N") + ".pdf");
            object document = null;
            try
            {
                dynamic docs = documents;
                document = docs.Open(FileName: source, ConfirmConversions: false, ReadOnly: true,
                    AddToRecentFiles: false, PasswordDocument: "", PasswordTemplate: "", Revert: false,
                    WritePasswordDocument: "", WritePasswordTemplate: "", Visible: false, NoEncodingDialog: true);
                dynamic doc = document;
                doc.ExportAsFixedFormat(OutputFileName: temporary, ExportFormat: 17, OpenAfterExport: false,
                    OptimizeFor: 0, Range: 0, Item: 0, IncludeDocProps: true, KeepIRM: true,
                    CreateBookmarks: 1, DocStructureTags: true, BitmapMissingFonts: true, UseISO19005_1: false);
                Files.CheckPdf(temporary);
                File.Move(temporary, output);
                return output;
            }
            finally
            {
                if (document != null)
                {
                    try { ((dynamic)document).Close(0); } catch { }
                    Marshal.FinalReleaseComObject(document);
                }
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        public void Dispose()
        {
            if (documents != null) { Marshal.FinalReleaseComObject(documents); documents = null; }
            if (application != null)
            {
                try { if (security >= 0) ((dynamic)application).AutomationSecurity = security; } catch { }
                try { ((dynamic)application).Quit(0); } catch { }
                Marshal.FinalReleaseComObject(application); application = null; security = -1;
            }
        }
    }

}
