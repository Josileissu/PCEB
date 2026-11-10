using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DEB.Revit.Presentation
{
    /// <summary>
    /// Leitor mínimo de DXF ASCII para os detalhes padrão do escritório (sem bibliotecas externas).
    /// Lê da seção ENTITIES: LINE, ARC, CIRCLE, LWPOLYLINE (com arcos), TEXT, MTEXT e SOLID; o resto é contado e ignorado
    /// (blocos, cotas e hachuras devem vir explodidos). Unidade do arquivo: cm do desenho na escala da legenda.
    /// Layers que o plugin reconhece: PCEB-ARMADURA, PCEB-CONTORNO, PCEB-FINA, PCEB-TRACEJADA, PCEB-COTA, PCEB-TEXTO,
    /// PCEB-PREENCHIMENTO; em outros layers, a cor 1 (vermelho) vale como armadura.
    /// </summary>
    internal sealed class DxfDetail
    {
        public enum Kind { Line, Arc, Circle, Text, Fill }

        public sealed class Prim
        {
            public Kind Kind;
            public string Layer = "0";
            public int Color = 256;
            public List<(double X, double Y)> Pts = new List<(double, double)>();
            public double R, A0, A1;            // arco: ângulos em radianos, anti-horário de A0 a A1
            public string Text;
            public double H, Rot;               // texto: altura (cm) e rotação (rad)
            public char HA = 'L', VA = 'B';     // L/C/R e B(linha de base)/M/T
        }

        public List<Prim> Items { get; } = new List<Prim>();
        public Dictionary<string, int> Ignored { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> LayerColors { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static DxfDetail Read(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            string text;
            try { text = new UTF8Encoding(false, true).GetString(raw); }
            catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(raw); }
            string[] lines = text.Replace("\r\n", "\n").Split('\n');

            var d = new DxfDetail();
            string section = null;
            List<(int, string)> cur = null;
            string curType = null;
            void Flush()
            {
                if (cur == null) return;
                if (section == "ENTITIES") d.Add(curType, cur);
                else if (section == "TABLES" && curType == "LAYER")
                {
                    string n = Get(cur, 2);
                    if (n != null) d.LayerColors[n] = Math.Abs(I(cur, 62, 7));
                }
                cur = null;
            }
            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                if (!int.TryParse(lines[i].Trim(), out int code)) continue;
                string v = lines[i + 1].Trim();
                if (code == 0)
                {
                    Flush();
                    if (v == "SECTION") { section = "?"; continue; }
                    if (v == "ENDSEC") { section = null; continue; }
                    if (v == "EOF") break;
                    curType = v;
                    cur = new List<(int, string)>();
                    continue;
                }
                if (section == "?" && code == 2) { section = v; continue; }
                cur?.Add((code, v));
            }
            Flush();
            return d;
        }

        // ------------------------------------------------------------------

        private static string Get(List<(int c, string v)> e, int code)
        {
            foreach (var (c, v) in e) if (c == code) return v;
            return null;
        }

        private static double F(List<(int, string)> e, int code, double def = 0) =>
            double.TryParse(Get(e, code), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : def;

        private static int I(List<(int, string)> e, int code, int def = 0) =>
            int.TryParse(Get(e, code), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ? x : def;

        private Prim New(Kind k, List<(int, string)> e) =>
            new Prim { Kind = k, Layer = Get(e, 8) ?? "0", Color = I(e, 62, 256) };

        private void Add(string type, List<(int c, string v)> e)
        {
            switch (type)
            {
                case "LINE":
                {
                    Prim p = New(Kind.Line, e);
                    p.Pts.Add((F(e, 10), F(e, 20)));
                    p.Pts.Add((F(e, 11), F(e, 21)));
                    Items.Add(p);
                    break;
                }
                case "CIRCLE":
                {
                    Prim p = New(Kind.Circle, e);
                    p.Pts.Add((F(e, 10), F(e, 20)));
                    p.R = F(e, 40);
                    Items.Add(p);
                    break;
                }
                case "ARC":
                {
                    Prim p = New(Kind.Arc, e);
                    p.Pts.Add((F(e, 10), F(e, 20)));
                    p.R = F(e, 40);
                    p.A0 = F(e, 50) * Math.PI / 180;
                    p.A1 = F(e, 51) * Math.PI / 180;
                    Items.Add(p);
                    break;
                }
                case "LWPOLYLINE":
                {
                    var verts = new List<(double X, double Y, double B)>();
                    foreach (var (c, v) in e)
                    {
                        if (c != 10 && c != 20 && c != 42) continue;
                        if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) continue;
                        if (c == 10) verts.Add((x, 0, 0));
                        else if (c == 20 && verts.Count > 0) verts[^1] = (verts[^1].X, x, verts[^1].B);
                        else if (c == 42 && verts.Count > 0) verts[^1] = (verts[^1].X, verts[^1].Y, x);
                    }
                    bool closed = (I(e, 70) & 1) == 1;
                    int n = verts.Count;
                    for (int j = 0; j < (closed ? n : n - 1); j++)
                        Segment(e, verts[j], verts[(j + 1) % n]);
                    break;
                }
                case "SOLID":
                {
                    Prim p = New(Kind.Fill, e);
                    // ordem do SOLID: 1, 2, 4, 3
                    foreach (int k in new[] { 0, 1, 3, 2 })
                        p.Pts.Add((F(e, 10 + k), F(e, 20 + k)));
                    Items.Add(p);
                    break;
                }
                case "TEXT":
                {
                    Prim p = New(Kind.Text, e);
                    int ha = I(e, 72), va = I(e, 73);
                    bool aligned = (ha != 0 || va != 0) && Get(e, 11) != null;
                    p.Pts.Add(aligned ? (F(e, 11), F(e, 21)) : (F(e, 10), F(e, 20)));
                    p.H = F(e, 40);
                    p.Rot = F(e, 50) * Math.PI / 180;
                    p.Text = Plain(Get(e, 1) ?? "");
                    p.HA = ha == 1 || ha == 4 ? 'C' : ha == 2 ? 'R' : 'L';
                    p.VA = va == 2 ? 'M' : va == 3 ? 'T' : 'B';
                    if (ha == 4) p.VA = 'M';
                    if (p.Text.Trim().Length > 0) Items.Add(p);
                    break;
                }
                case "MTEXT":
                {
                    Prim p = New(Kind.Text, e);
                    var sb = new StringBuilder();
                    foreach (var (c, v) in e) if (c == 3) sb.Append(v);
                    sb.Append(Get(e, 1) ?? "");
                    p.Text = Plain(MText(sb.ToString()));
                    p.Pts.Add((F(e, 10), F(e, 20)));
                    p.H = F(e, 40);
                    p.Rot = Get(e, 50) != null ? F(e, 50) * Math.PI / 180
                          : Get(e, 11) != null ? Math.Atan2(F(e, 21), F(e, 11)) : 0;
                    int att = Math.Max(1, I(e, 71, 1));
                    p.HA = "LCR"[(att - 1) % 3];
                    p.VA = "TMB"[(att - 1) / 3 % 3];
                    if (p.VA == 'B') p.VA = 'b';     // base da caixa do MTEXT (não a linha de base)
                    if (p.Text.Trim().Length > 0) Items.Add(p);
                    break;
                }
                default:
                    Ignored[type] = Ignored.TryGetValue(type, out int k2) ? k2 + 1 : 1;
                    break;
            }
        }

        /// <summary>Trecho de polilinha: reta, ou arco pelo "bulge" (tangente de 1/4 do ângulo).</summary>
        private void Segment(List<(int, string)> e, (double X, double Y, double B) p0, (double X, double Y, double B) p1)
        {
            if (Math.Abs(p0.B) < 1e-9)
            {
                Prim l = New(Kind.Line, e);
                l.Pts.Add((p0.X, p0.Y));
                l.Pts.Add((p1.X, p1.Y));
                Items.Add(l);
                return;
            }
            double th = 4 * Math.Atan(p0.B);
            double dx = p1.X - p0.X, dy = p1.Y - p0.Y, ch = Math.Sqrt(dx * dx + dy * dy);
            if (ch < 1e-9) return;
            double r = ch / (2 * Math.Sin(Math.Abs(th) / 2));
            double h = Math.Sqrt(Math.Max(r * r - ch * ch / 4, 0));
            double nx = -dy / ch, ny = dx / ch;
            int sgn = (p0.B > 0) == (Math.Abs(th) < Math.PI) ? 1 : -1;
            double cx = (p0.X + p1.X) / 2 + sgn * nx * h, cy = (p0.Y + p1.Y) / 2 + sgn * ny * h;
            double a0 = Math.Atan2(p0.Y - cy, p0.X - cx), a1 = Math.Atan2(p1.Y - cy, p1.X - cx);
            if (p0.B < 0) (a0, a1) = (a1, a0);
            Prim a = New(Kind.Arc, e);
            a.Pts.Add((cx, cy));
            a.R = r;
            a.A0 = a0;
            a.A1 = a1;
            Items.Add(a);
        }

        private static readonly Regex MTextCodes = new Regex(@"\\[ACcFfHhQqTtWwp][^;]*;|\\[LlOoKk]|[{}]", RegexOptions.Compiled);

        private static string MText(string s)
        {
            s = s.Replace("\\P", "\n").Replace("\\~", " ").Replace("^I", "    ");
            s = Regex.Replace(s, @"\\S([^;^]*)[\^#/]([^;]*);", "$1/$2");
            return MTextCodes.Replace(s, "").Replace("\\\\", "\\");
        }

        /// <summary>Códigos de controle do AutoCAD (%%c, \U+XXXX).</summary>
        public static string Plain(string s)
        {
            s = s.Replace("%%c", "ø").Replace("%%C", "ø").Replace("%%d", "°").Replace("%%D", "°")
                 .Replace("%%p", "±").Replace("%%P", "±").Replace("%%u", "").Replace("%%U", "");
            return Regex.Replace(s, @"\\U\+([0-9A-Fa-f]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        }
    }
}
