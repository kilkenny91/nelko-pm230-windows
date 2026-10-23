// PM230 Label-Studio: native Windows-GUI (WinForms) fuer den Nelko PM230.
// Wird zusammen mit pm230.cs kompiliert (nutzt Tspl + SerialLink von dort).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

class Elem
{
    public string kind = "text";          // "text" | "img"
    public double x, y, w, h;             // mm, relativ zum Druckfeld
    public double rot;                    // Grad
    public string text = "";
    public string font = "Segoe UI";
    public double sizeMM = 6, lh = 1.2;
    public bool bold;
    public string align = "left";
    public Bitmap img;
    public string name = "Bild";
    public List<string> pdf;              //datenurl-Dateien (PNG) aller Seiten
    public int page;
}

class PMStudio
{
    const int MM = 8;                      // dots per mm (203 dpi)
    const int WD = 384;                    // Breite des Druckkopfs in dots
    const int PADX = 24, PADY = 16;        // 3 mm / 2 mm Papier ausserhalb des Druckfeldes
    const string DEVNAME = "PM230";

    Form fm;
    Panel host, canvas, top, left, right;
    StatusStrip strip;
    ToolStripStatusLabel stStatus, stInfo;
    ListBox layers;
    ComboBox paperPreset, fontBox, zoomBox;
    NumericUpDown numHeight, numCopies, numSize, numImgW, numImgH, numX, numY;
    TrackBar trDensity, trLh, trRot, trContrast;
    Label lbDensity, lbLh, lbRot, lbContrast, lbPage, lbBattery;
    CheckBox chDither, chInvert, chBold, chKeepAspect;
    TextBox txt;
    Button btnPrint, btnPrev, btnNext;
    Panel pdfRow;

    List<Elem> els = new List<Elem>();
    Elem sel;
    bool autoHeight = true;
    double zoom = 2.5, snap = 0.5, contrast = 1.0;
    int density = 10, copies = 1;
    bool dither = true, invert = false, keepAspect = true, loading;
    int lastId = 1;
    Bitmap[] pdfPages = new Bitmap[0];
    int pdfIdx;
    string linkName = "-";
    System.Windows.Forms.Timer poll;

    static readonly string[] FONTS = { "Segoe UI", "Segoe UI Black", "Arial", "Arial Black", "Verdana",
        "Tahoma", "Georgia", "Times New Roman", "Courier New", "Consolas", "Impact", "Bahnschrift",
        "Franklin Gothic Medium", "Comic Sans MS" };

    static Bitmap measureBmp = new Bitmap(1, 1);
    static Graphics measureG = Graphics.FromImage(measureBmp);

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        PMStudio app = new PMStudio();
        app.Run();
    }

    void Run()
    {
        BuildShell();
        LoadIcon();
        Elem e = new Elem();
        e.kind = "text"; e.text = "Etikett"; e.sizeMM = 9; e.bold = true; e.align = "center";
        e.x = 0; e.y = 4;
        els.Add(e);
        Select(e);
        RefreshCanvasSize();
        RefreshStatus();
        poll = new System.Windows.Forms.Timer();
        poll.Interval = 15000;
        poll.Tick += delegate { RefreshStatus(); };
        poll.Start();
        Application.Run(fm);
    }

    // ---------------- aufbau ----------------

    static T Put<T>(Panel p, T c, int x, int y, int w) where T : Control
    {
        c.Location = new Point(x, y);
        if (w > 0) c.Width = w;
        p.Controls.Add(c);
        return c;
    }

    static Label Lbl(string text, int x, int y, int w)
    {
        Label l = new Label();
        l.Text = text; l.Location = new Point(x, y); l.AutoSize = false; l.Size = new Size(w, 18);
        l.ForeColor = Color.FromArgb(150, 160, 178);
        return l;
    }

    static Button Btn(string text, int w)
    {
        Button b = new Button();
        b.Text = text; b.Size = new Size(w, 26);
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = Color.FromArgb(40, 47, 60);
        b.ForeColor = Color.White;
        return b;
    }

    static CheckBox Chk(string text)
    {
        CheckBox c = new CheckBox();
        c.Text = text; c.AutoSize = true; c.ForeColor = Color.White;
        return c;
    }

    static TrackBar Tr(int min, int max, int val, int w)
    {
        TrackBar t = new TrackBar();
        t.Minimum = min; t.Maximum = max; t.Value = val; t.TickStyle = TickStyle.None;
        t.Width = w; t.AutoSize = false; t.Height = 30;
        t.BackColor = Color.FromArgb(28, 32, 40);
        return t;
    }

    static NumericUpDown Nd(decimal min, decimal max, decimal val, int decimals, int w)
    {
        NumericUpDown n = new NumericUpDown();
        n.Minimum = min; n.Maximum = max; n.Value = val; n.DecimalPlaces = decimals;
        n.Width = w; n.BackColor = Color.FromArgb(35, 40, 51); n.ForeColor = Color.White;
        return n;
    }

    void BuildShell()
    {
        fm = new Form();
        fm.Text = "PM230 Label-Studio – Etiketten drucken";
        fm.ClientSize = new Size(1200, 780);
        fm.StartPosition = FormStartPosition.CenterScreen;
        fm.BackColor = Color.FromArgb(20, 23, 28);
        fm.KeyPreview = true;
        fm.KeyDown += delegate (object o, KeyEventArgs ke)
        {
            if (ke.Control && ke.KeyCode == Keys.P) { DoPrint(); ke.Handled = true; }
        };

        host = new Panel();
        host.Dock = DockStyle.Fill;
        host.AutoScroll = true;
        host.BackColor = Color.FromArgb(14, 17, 22);

        canvas = new DoublePanel();
        canvas.BackColor = Color.FromArgb(14, 17, 22);
        canvas.TabStop = true;
        host.Controls.Add(canvas);
        canvas.MouseDown += OnDown;
        canvas.MouseMove += OnMove;
        canvas.MouseUp += OnUp;
        canvas.Paint += OnPaint;
        canvas.KeyDown += OnKey;

        top = new Panel();
        top.Dock = DockStyle.Top;
        top.Height = 52;
        top.BackColor = Color.FromArgb(28, 32, 40);

        int x = 10;

        Put(top, Lbl("Papier", x, 14, 44), x, 14, 0); x += 46;
        paperPreset = new ComboBox();
        paperPreset.DropDownStyle = ComboBoxStyle.DropDownList;
        paperPreset.Items.AddRange(new object[] { "54 x 25 mm", "54 x 40 mm", "54 x 60 mm", "54 mm Endlos" });
        paperPreset.SelectedIndex = 3;
        paperPreset.Width = 104;
        Put(top, paperPreset, x, 11, 0); x += 110;
        paperPreset.SelectedIndexChanged += delegate
        {
            if (loading) return;
            loading = true;
            if (paperPreset.SelectedIndex == 3) autoHeight = true;
            else { autoHeight = false; numHeight.Value = 25 + 15 * paperPreset.SelectedIndex; }
            numHeight.Enabled = paperPreset.SelectedIndex != 3;
            loading = false;
            RefreshCanvasSize();
        };

        Put(top, Lbl("Höhe mm", x, 14, 56), x, 14, 0); x += 58;
        numHeight = Nd(5, 400, 40, 0, 50);
        Put(top, numHeight, x, 11, 0); x += 56;
        numHeight.ValueChanged += delegate { if (loading) return; autoHeight = false; RefreshCanvasSize(); };

        Put(top, Lbl("Schwärze", x, 14, 56), x, 14, 0); x += 58;
        trDensity = Tr(0, 15, 10, 90);
        Put(top, trDensity, x, 10, 0); x += 92;
        lbDensity = Lbl("10", x, 14, 24); Put(top, lbDensity, x, 14, 0); x += 30;
        trDensity.Scroll += delegate { density = trDensity.Value; lbDensity.Text = density.ToString(); };

        Put(top, Lbl("Kopien", x, 14, 42), x, 14, 0); x += 44;
        numCopies = Nd(1, 99, 1, 0, 44); Put(top, numCopies, x, 11, 0); x += 50;
        numCopies.ValueChanged += delegate { copies = (int)numCopies.Value; };

        chDither = Chk("Weichzeichnung"); chDither.Checked = true; chDither.Location = new Point(x, 15);
        top.Controls.Add(chDither); chDither.CheckedChanged += delegate { dither = chDither.Checked; }; x += 112;
        chInvert = Chk("invertiert"); chInvert.Location = new Point(x, 15);
        top.Controls.Add(chInvert); chInvert.CheckedChanged += delegate { canvas.Invalidate(); }; x += 90;

        btnPrint = Btn("DRUCKEN", 100);
        btnPrint.BackColor = Color.FromArgb(62, 166, 255);
        btnPrint.ForeColor = Color.FromArgb(5, 36, 61);
        btnPrint.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        Put(top, btnPrint, x, 11, 0); x += 108;
        btnPrint.Click += delegate { DoPrint(); };

        lbBattery = Lbl("Drucker wird geprüft ...", x, 14, 260);
        lbBattery.ForeColor = Color.FromArgb(150, 160, 178);
        Put(top, lbBattery, x, 14, 0);

        left = new Panel();
        left.Dock = DockStyle.Left;
        left.Width = 194;
        left.BackColor = Color.FromArgb(28, 32, 40);
        left.AutoScroll = true;
        Put(left, Lbl("EINFÜGEN", 10, 8, 174), 10, 8, 0);
        Button bt = Btn("Text", 56); bt.Click += delegate { AddText(); }; bt.Location = new Point(10, 26); left.Controls.Add(bt);
        Button bi = Btn("Bild", 56); bi.Click += delegate { AddImage(); }; bi.Location = new Point(69, 26); left.Controls.Add(bi);
        Button bp = Btn("PDF", 56); bp.Click += delegate { AddPdf(); }; bp.Location = new Point(128, 26); left.Controls.Add(bp);
        Label ihint = Lbl("Text tippen, Bild- oder PDF-Datei wählen", 10, 56, 174);
        ihint.Size = new Size(174, 30);
        left.Controls.Add(ihint);
        Put(left, Lbl("EBENEN", 10, 90, 174), 10, 90, 0);
        layers = new ListBox();
        layers.Location = new Point(10, 110);
        layers.Size = new Size(174, 190);
        layers.BackColor = Color.FromArgb(35, 40, 51);
        layers.ForeColor = Color.White;
        layers.IntegralHeight = false;
        left.Controls.Add(layers);
        layers.SelectedIndexChanged += delegate
        {
            if (loading) return;
            int i = layers.SelectedIndex;
            if (i >= 0 && i < els.Count) Select(els[i]);
        };
        Button bd = Btn("duplizieren", 85); bd.Click += delegate { Duplicate(); }; Put(left, bd, 10, 308, 0);
        Button bx = Btn("löschen", 85); bx.Click += delegate { DeleteSel(); }; Put(left, bx, 99, 308, 0);
        Button bf = Btn("nach vorne", 85); bf.Click += delegate { Order(true); }; Put(left, bf, 10, 338, 0);
        Button bb = Btn("nach hinten", 85); bb.Click += delegate { Order(false); }; Put(left, bb, 99, 338, 0);
        Put(left, Lbl("Zoom", 10, 376, 40), 10, 376, 0);
        zoomBox = new ComboBox();
        zoomBox.DropDownStyle = ComboBoxStyle.DropDownList;
        zoomBox.Items.AddRange(new object[] { "1x", "1.5x", "2x", "2.5x", "3x", "4x", "6x", "8x" });
        zoomBox.SelectedIndex = 2;
        Button fitz = Btn("einpassen", 62);
        fitz.Click += delegate { FitZoom(); };
        Put(left, fitz, 116, 373, 0);
        Put(left, zoomBox, 52, 373, 58);
        zoomBox.SelectedIndexChanged += delegate
        {
            string s = zoomBox.SelectedItem.ToString();
            zoom = double.Parse(s.TrimEnd('x'), System.Globalization.CultureInfo.InvariantCulture);
            RefreshCanvasSize();
        };
        CheckBox snappy = Chk("Raster 0,5 mm"); snappy.Checked = true;
        snappy.Location = new Point(10, 408); left.Controls.Add(snappy);
        snappy.CheckedChanged += delegate { snap = snappy.Checked ? 0.5 : 0; };
        Label hint = Lbl("Pfeiltasten = 1 mm\nShift = 0,1 mm\nEntf = löschen\nStrg+D = duplizieren\nStrg+P = drucken", 10, 436, 174);
        hint.Size = new Size(174, 92);
        Put(left, hint, 10, 436, 0);

        right = new Panel();
        right.Dock = DockStyle.Right;
        right.Width = 300;
        right.BackColor = Color.FromArgb(28, 32, 40);
        right.AutoScroll = true;

        int ry = 8;
        Put(right, Lbl("TEXT", 10, ry, 200), 10, ry, 0); ry += 20;
        txt = new TextBox();
        txt.Multiline = true; txt.ScrollBars = ScrollBars.Vertical;
        txt.Location = new Point(10, ry); txt.Size = new Size(276, 66);
        txt.BackColor = Color.FromArgb(35, 40, 51); txt.ForeColor = Color.White;
        right.Controls.Add(txt); ry += 72;
        txt.TextChanged += delegate { Elem e = Sel(); if (e != null && e.kind == "text" && !loading) { e.text = txt.Text; Touch(); } };

        fontBox = new ComboBox();
        fontBox.DropDownStyle = ComboBoxStyle.DropDownList;
        fontBox.Items.AddRange(FONTS);
        Put(right, fontBox, 10, ry, 160);
        Put(right, Lbl("fett", 178, ry + 5, 30), 178, ry + 5, 0);
        chBold = Chk(""); chBold.Location = new Point(212, ry + 3); right.Controls.Add(chBold);
        fontBox.SelectedIndexChanged += delegate { Elem e = Sel(); if (e != null && !loading) { e.font = fontBox.SelectedItem.ToString(); Touch(); } };
        chBold.CheckedChanged += delegate { Elem e = Sel(); if (e != null && !loading) { e.bold = chBold.Checked; Touch(); } };
        ry += 32;

        Put(right, Lbl("Größe mm", 10, ry + 3, 66), 10, ry + 3, 0);
        numSize = Nd(1, 60, 6, 1, 54); Put(right, numSize, 80, ry, 0);
        numSize.ValueChanged += delegate { Elem e = Sel(); if (e != null && e.kind == "text" && !loading) { e.sizeMM = (double)numSize.Value; Touch(); } };
        ry += 30;

        Put(right, Lbl("Zeilenabstand", 10, ry + 6, 80), 10, ry + 6, 0);
        trLh = Tr(80, 250, 120, 120); Put(right, trLh, 96, ry, 0);
        lbLh = Lbl("1.20", 224, ry + 6, 40); Put(right, lbLh, 224, ry + 6, 0);
        trLh.Scroll += delegate { Elem e = Sel(); if (e != null && !loading) { e.lh = trLh.Value / 100.0; lbLh.Text = e.lh.ToString("0.00"); Touch(); } };
        ry += 36;

        int ax = 10;
        for (int i = 0; i < 3; i++)
        {
            CheckBox c = new CheckBox();
            c.Appearance = Appearance.Button;
            c.Text = new string[] { "links", "Mitte", "rechts" }[i];
            c.TextAlign = ContentAlignment.MiddleCenter;
            c.Location = new Point(ax, ry); c.Size = new Size(88, 26);
            c.BackColor = Color.FromArgb(40, 47, 60); c.ForeColor = Color.White;
            right.Controls.Add(c);
            string tag = new string[] { "left", "center", "right" }[i];
            c.Click += delegate { Elem e = Sel(); if (e != null && !loading) { e.align = tag; Touch(); } };
            alignBoxes.Add(c);
            ax += 90;
        }
        ry += 34;

        Put(right, Lbl("BILD / SEITE", 10, ry, 200), 10, ry, 0); ry += 20;
        Put(right, Lbl("Breite mm", 10, ry + 3, 60), 10, ry + 3, 0);
        numImgW = Nd(1, 400, 48, 1, 54); Put(right, numImgW, 74, ry, 0);
        Put(right, Lbl("Höhe", 138, ry + 3, 40), 138, ry + 3, 0);
        numImgH = Nd(1, 400, 40, 1, 54); Put(right, numImgH, 182, ry, 0);
        numImgW.ValueChanged += delegate { Elem e = Sel(); if (e != null && e.kind == "img" && !loading) { double r = e.h / e.w; e.w = (double)numImgW.Value; if (keepAspect) e.h = e.w * r; Touch(); } };
        numImgH.ValueChanged += delegate { Elem e = Sel(); if (e != null && e.kind == "img" && !loading) { double r = e.w / e.h; e.h = (double)numImgH.Value; if (keepAspect) e.w = e.h * r; Touch(); } };
        ry += 30;
        chKeepAspect = Chk("Seitenverhältnis halten"); chKeepAspect.Checked = true;
        chKeepAspect.Location = new Point(10, ry); right.Controls.Add(chKeepAspect);
        chKeepAspect.CheckedChanged += delegate { keepAspect = chKeepAspect.Checked; };
        ry += 26;

        Put(right, Lbl("Kontrast", 10, ry + 6, 56), 10, ry + 6, 0);
        trContrast = Tr(40, 250, 100, 130); Put(right, trContrast, 70, ry, 0);
        lbContrast = Lbl("1.00", 208, ry + 6, 40); Put(right, lbContrast, 208, ry + 6, 0);
        trContrast.Scroll += delegate { contrast = trContrast.Value / 100.0; lbContrast.Text = contrast.ToString("0.00"); canvas.Invalidate(); };
        ry += 34;

        pdfRow = new Panel();
        pdfRow.Location = new Point(10, ry); pdfRow.Size = new Size(280, 30);
        pdfRow.Visible = false; right.Controls.Add(pdfRow);
        btnPrev = Btn("< Seite", 84); btnPrev.Click += delegate { PageStep(-1); }; Put(pdfRow, btnPrev, 0, 2, 0);
        btnNext = Btn("Seite >", 84); btnNext.Click += delegate { PageStep(1); }; Put(pdfRow, btnNext, 168, 2, 0);
        lbPage = Lbl("1", 96, 8, 60); Put(pdfRow, lbPage, 96, 8, 0);
        ry += 36;

        Put(right, Lbl("DREHEN und POSITION", 10, ry, 220), 10, ry, 0); ry += 20;
        trRot = Tr(0, 359, 0, 150); Put(right, trRot, 10, ry, 0);
        lbRot = Lbl("0", 168, ry + 6, 40); Put(right, lbRot, 168, ry + 6, 0);
        Put(right, Lbl("Grad", 212, ry + 6, 40), 212, ry + 6, 0);
        trRot.Scroll += delegate { Elem e = Sel(); if (e != null && !loading) { e.rot = trRot.Value; lbRot.Text = ((int)e.rot).ToString(); Touch(); } };
        ry += 36;

        string[] pos = { "tl", "tc", "tr", "ml", "cc", "mr", "bl", "bc", "br" };
        string[] posTxt = { "hoch links", "hoch Mitte", "hoch rechts", "Mitte links", "zentriert", "Mitte rechts", "runter links", "runter Mitte", "runter rechts" };
        for (int i = 0; i < 9; i++)
        {
            Button b = Btn(posTxt[i], 88);
            b.Location = new Point(10 + (i % 3) * 92, ry + (i / 3) * 28);
            right.Controls.Add(b);
            string p = pos[i];
            b.Click += delegate { AlignTo(p); };
        }
        ry += 90;

        Put(right, Lbl("x mm", 10, ry + 3, 34), 10, ry + 3, 0);
        numX = Nd(-40, 200, 0, 1, 58); Put(right, numX, 46, ry, 0);
        Put(right, Lbl("y mm", 116, ry + 3, 34), 116, ry + 3, 0);
        numY = Nd(-40, 400, 0, 1, 58); Put(right, numY, 152, ry, 0);
        numX.ValueChanged += delegate { Elem e = Sel(); if (e != null && !loading) { e.x = (double)numX.Value; TouchNoMeasure(); } };
        numY.ValueChanged += delegate { Elem e = Sel(); if (e != null && !loading) { e.y = (double)numY.Value; TouchNoMeasure(); } };
        ry += 32;
        Button fw = Btn("volle Breite", 134); fw.Click += delegate { Fit(true); }; Put(right, fw, 10, ry, 0);
        Button fh = Btn("auf Feldhöhe", 134); fh.Click += delegate { Fit(false); }; Put(right, fh, 152, ry, 0);

        strip = new StatusStrip();
        strip.Renderer = new ToolStripProfessionalRenderer(new DarkTable());
        strip.BackColor = Color.FromArgb(24, 28, 35);
        strip.SizingGrip = false;
        stStatus = new ToolStripStatusLabel("bereit");
        stInfo = new ToolStripStatusLabel("");
        stStatus.ForeColor = Color.FromArgb(226, 232, 240);
        stInfo.ForeColor = Color.FromArgb(150, 160, 178);
        stStatus.Font = new Font("Segoe UI", 9f);
        stInfo.Spring = true;
        stInfo.TextAlign = ContentAlignment.MiddleRight;
        strip.Items.Add(stStatus);
        strip.Items.Add(stInfo);

        fm.Controls.Add(host);
        fm.Controls.Add(left);
        fm.Controls.Add(right);
        fm.Controls.Add(top);
        fm.Controls.Add(strip);
        host.BringToFront();
        canvas.Focus();
    }

    List<CheckBox> alignBoxes = new List<CheckBox>();

    // ---------------- elemente und eigenschaften ----------------

    void LoadIcon()
    {
        try
        {
            System.Reflection.Assembly a = System.Reflection.Assembly.GetExecutingAssembly();
            string[] names = a.GetManifestResourceNames();
            for (int i = 0; i < names.Length; i++)
            {
                if (!names[i].EndsWith(".ico")) continue;
                using (System.IO.Stream st = a.GetManifestResourceStream(names[i]))
                {
                    if (st != null) { fm.Icon = new Icon(st); return; }
                }
            }
        }
        catch (Exception) { }
        try { fm.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch (Exception) { }
    }

    Elem Sel() { return sel; }

    void Touch() { canvas.Invalidate(); UpdateInfo(); }
    void TouchNoMeasure() { canvas.Invalidate(); UpdateInfo(); }

    void AddText()
    {
        Elem e = new Elem();
        e.kind = "text"; e.text = "Neuer Text"; e.sizeMM = 6; e.x = 2; e.y = 2;
        els.Add(e); Select(e);
    }

    void AddImage()
    {
        OpenFileDialog d = new OpenFileDialog();
        d.Filter = "Bilder|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Alle Dateien|*.*";
        d.Title = "Bild einfuegen";
        if (d.ShowDialog(fm) != DialogResult.OK) return;
        Bitmap bmp;
        using (Bitmap t = new Bitmap(d.FileName)) bmp = new Bitmap(t);
        Elem e = new Elem();
        e.kind = "img"; e.img = bmp; e.name = Path.GetFileName(d.FileName);
        double natMM = bmp.Width / (double)MM;
        e.w = Math.Min(WD / (double)MM, natMM);
        e.h = e.w * bmp.Height / (double)bmp.Width;
        e.x = (WD / (double)MM - e.w) / 2; e.y = 2;
        els.Add(e); Select(e);
    }

    void AddPdf()
    {
        OpenFileDialog d = new OpenFileDialog();
        d.Filter = "PDF|*.pdf|Alle Dateien|*.*";
        d.Title = "PDF einfuegen (Seiten werden mit 203 dpi gerendert)";
        if (d.ShowDialog(fm) != DialogResult.OK) return;
        stStatus.Text = "PDF wird gerendert ...";
        fm.Cursor = Cursors.WaitCursor;
        Bitmap[] pages = RenderPdf(d.FileName);
        fm.Cursor = Cursors.Default;
        if (pages.Length == 0) { stStatus.Text = "PDF konnte nicht gelesen werden"; return; }
        pdfPages = pages; pdfIdx = 0;
        Elem e = new Elem();
        e.kind = "img"; e.img = pages[0]; e.name = Path.GetFileName(d.FileName);
        e.pdf = new List<string>();
        e.w = WD / (double)MM;
        e.h = e.w * pages[0].Height / (double)pages[0].Width;
        e.x = 0; e.y = 0;
        els.Add(e);
        Select(e);
        stStatus.Text = pages.Length + " PDF-Seite(n) eingefuegt";
    }

    void PageStep(int d)
    {
        Elem e = Sel();
        if (e == null || pdfPages.Length == 0) return;
        pdfIdx = (pdfIdx + d + pdfPages.Length) % pdfPages.Length;
        e.img = pdfPages[pdfIdx];
        double ratio = e.h / e.w;
        e.w = WD / (double)MM; e.h = e.w * pdfPages[pdfIdx].Height / (double)pdfPages[pdfIdx].Width;
        lbPage.Text = (pdfIdx + 1) + " / " + pdfPages.Length;
        Touch();
    }

    void Select(Elem e)
    {
        sel = e;
        loading = true;
        RefreshLayers();
        if (e != null)
        {
            Measure(e);
            if (e.kind == "text") { txt.Text = e.text; fontBox.SelectedItem = e.font; if (fontBox.SelectedIndex < 0) { fontBox.Items.Add(e.font); fontBox.SelectedItem = e.font; } numSize.Value = Clamp(numSize, (decimal)e.sizeMM); chBold.Checked = e.bold; trLh.Value = (int)Math.Round(e.lh * 100); lbLh.Text = e.lh.ToString("0.00"); }
            for (int i = 0; i < alignBoxes.Count; i++) alignBoxes[i].Checked = (e.align == new string[] { "left", "center", "right" }[i]);
            numImgW.Value = Clamp(numImgW, (decimal)e.w);
            numImgH.Value = Clamp(numImgH, (decimal)e.h);
            trRot.Value = (int)((e.rot % 360 + 360) % 360);
            lbRot.Text = ((int)e.rot).ToString();
            numX.Value = Clamp(numX, (decimal)e.x);
            numY.Value = Clamp(numY, (decimal)e.y);
        }
        txt.Enabled = e != null && e.kind == "text";
        fontBox.Enabled = e != null && e.kind == "text";
        numSize.Enabled = e != null && e.kind == "text";
        chBold.Enabled = e != null && e.kind == "text";
        for (int i = 0; i < alignBoxes.Count; i++) alignBoxes[i].Enabled = e != null && e.kind == "text";
        numImgW.Enabled = e != null && e.kind == "img";
        numImgH.Enabled = e != null && e.kind == "img";
        pdfRow.Visible = e != null && e.kind == "img" && pdfPages.Length > 0;
        loading = false;
        canvas.Invalidate();
        UpdateInfo();
    }

    static decimal Clamp(NumericUpDown n, decimal v)
    {
        if (v < n.Minimum) return n.Minimum;
        if (v > n.Maximum) return n.Maximum;
        return v;
    }

    void RefreshLayers()
    {
        int keep = layers.SelectedIndex;
        layers.Items.Clear();
        foreach (Elem e in els)
        {
            string t = e.kind == "text"
                ? "T  " + (e.text.Length > 0 ? e.text.Split('\n')[0] : "")
                : "B  " + e.name;
            if (t.Length > 22) t = t.Substring(0, 22) + "...";
            layers.Items.Add(t);
        }
        if (sel != null) layers.SelectedIndex = els.IndexOf(sel);
        else layers.SelectedIndex = -1;
    }

    void Duplicate()
    {
        if (sel == null) return;
        Elem c = new Elem();
        c.kind = sel.kind; c.x = sel.x + 2; c.y = sel.y + 2; c.w = sel.w; c.h = sel.h; c.rot = sel.rot;
        c.text = sel.text; c.font = sel.font; c.sizeMM = sel.sizeMM; c.bold = sel.bold; c.lh = sel.lh; c.align = sel.align;
        c.img = sel.img; c.name = sel.name;
        els.Add(c); Select(c);
    }

    void DeleteSel()
    {
        if (sel == null) return;
        els.Remove(sel);
        Select(els.Count > 0 ? els[els.Count - 1] : null);
    }

    void Order(bool front)
    {
        if (sel == null) return;
        els.Remove(sel);
        if (front) els.Add(sel); else els.Insert(0, sel);
        canvas.Invalidate();
    }

    void AlignTo(string p)
    {
        if (sel == null) return;
        Measure(sel);
        double W = WD / (double)MM, H = LabelDots() / (double)MM;
        char vr = p[0], hr = p[p.Length - 1];
        if (hr == 'l') sel.x = 0;
        else if (hr == 'r') sel.x = W - sel.w;
        else sel.x = (W - sel.w) / 2;
        if (vr == 't') sel.y = 0;
        else if (vr == 'b') sel.y = H - sel.h;
        else sel.y = (H - sel.h) / 2;
        sel.x = Math.Round(sel.x * 10) / 10; sel.y = Math.Round(sel.y * 10) / 10;
        loading = true; numX.Value = Clamp(numX, (decimal)sel.x); numY.Value = Clamp(numY, (decimal)sel.y); loading = false;
        canvas.Invalidate(); UpdateInfo();
    }

    void Fit(bool width)
    {
        if (sel == null) return;
        Measure(sel);
        if (width) { double r = sel.h / sel.w; sel.w = WD / (double)MM; sel.h = sel.w * r; }
        else { double r = sel.w / sel.h; sel.h = LabelDots() / (double)MM; sel.w = sel.h * r; }
        sel.x = 0; sel.y = 0;
        loading = true;
        numImgW.Value = Clamp(numImgW, (decimal)sel.w); numImgH.Value = Clamp(numImgH, (decimal)sel.h);
        numX.Value = 0; numY.Value = 0;
        loading = false;
        canvas.Invalidate(); UpdateInfo();
    }

    // ---------------- geometrie ----------------

    int LabelDots()
    {
        if (autoHeight)
        {
            double b = 0;
            foreach (Elem e in els) { Measure(e); if (e.y + e.h > b) b = e.y + e.h; }
            return Math.Max(8 * MM, (int)Math.Ceiling((b + 1.0) * MM));
        }
        return (int)Math.Round(numHeight.Value * MM);
    }

    void UpdateInfo()
    {
        int H = LabelDots();
        double l = 1e9, t = 1e9, r = 0, b = 0;
        foreach (Elem e in els) { Measure(e); l = Math.Min(l, e.x); t = Math.Min(t, e.y); r = Math.Max(r, e.x + e.w); b = Math.Max(b, e.y + e.h); }
        if (els.Count == 0) { l = 0; t = 0; r = WD / (double)MM; b = H / (double)MM; }
        stInfo.Text = "Druckfeld 48 mm (" + WD + " dots) x " + (H / (double)MM).ToString("0.#") +
            " mm  ·  Inhalt " + (r - l).ToString("0.#") + " x " + (b - t).ToString("0.#") +
            " mm  ·  " + els.Count + " Element(e)" + (linkName == "-" ? "" : "  ·  " + linkName);
    }

    void FitZoom()
    {
        int H = LabelDots();
        double zx = (host.ClientSize.Width - 24) / (double)(WD + 2 * PADX);
        double zy = (host.ClientSize.Height - 24) / (double)(H + 2 * PADY);
        double z = Math.Min(zx, zy);
        if (z < 1) z = 1;
        if (z > 8) z = 8;
        zoom = Math.Round(z * 4) / 4;
        string want = zoom + "x";
        for (int i = 0; i < zoomBox.Items.Count; i++)
            if (zoomBox.Items[i].ToString() == want) { loading = true; zoomBox.SelectedIndex = i; loading = false; break; }
        RefreshCanvasSize();
    }

    void RefreshCanvasSize()
    {
        int H = LabelDots();
        int w = (int)Math.Round((WD + 2 * PADX) * zoom);
        int h = (int)Math.Round((H + 2 * PADY) * zoom);
        canvas.SuspendLayout();
        canvas.Size = new Size(w, h);
        canvas.MinimumSize = new Size(w, h);
        canvas.MaximumSize = new Size(w, h);
        canvas.ResumeLayout();
        canvas.Invalidate();
        UpdateInfo();
    }

    void Measure(Elem e)
    {
        if (e.kind != "text") return;
        float px = (float)(e.sizeMM * MM);
        using (Font f = new Font(e.font, px, e.bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
        {
            string[] lines = (e.text ?? "").Replace("\r\n", "\n").Split('\n');
            double w = 0;
            foreach (string s in lines)
            {
                SizeF sz = measureG.MeasureString(s.Length == 0 ? " " : s, f, new PointF(0, 0), StringFormat.GenericTypographic);
                if (sz.Width > w) w = sz.Width;
            }
            e.w = Math.Max(1, w / MM);
            e.h = Math.Max(1, lines.Length * px * e.lh / MM);
        }
    }

    void DrawElements(Graphics g, double z)
    {
        foreach (Elem e in els)
        {
            Measure(e);
            GraphicsState st = g.Save();
            g.TranslateTransform((float)((e.x + e.w / 2) * MM * z), (float)((e.y + e.h / 2) * MM * z));
            if (e.rot != 0) g.RotateTransform((float)e.rot);
            if (e.kind == "img" && e.img != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(e.img, (float)(-e.w / 2 * MM * z), (float)(-e.h / 2 * MM * z), (float)(e.w * MM * z), (float)(e.h * MM * z));
            }
            else
            {
                float px = (float)(e.sizeMM * MM * z);
                using (Font f = new Font(e.font, px, e.bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    string[] lines = (e.text ?? "").Replace("\r\n", "\n").Split('\n');
                    float lh = px * (float)e.lh;
                    float top = (float)(-e.h / 2 * MM * z);
                    StringFormat sf = StringFormat.GenericTypographic;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        SizeF sz = g.MeasureString(lines[i], f, new PointF(0, 0), sf);
                        float x0 = (float)(-e.w / 2 * MM * z);
                        if (e.align == "center") x0 = -sz.Width / 2;
                        if (e.align == "right") x0 = (float)(e.w / 2 * MM * z) - sz.Width;
                        g.DrawString(lines[i], f, Brushes.Black, new RectangleF(x0, top + i * lh, 20000, lh * 3), sf);
                    }
                }
            }
            g.Restore(st);
        }
    }

    // ---------------- malen ----------------

    void OnPaint(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int H = LabelDots();
        float z = (float)zoom;
        g.Clear(Color.FromArgb(14, 17, 22));
        using (SolidBrush paper = new SolidBrush(Color.FromArgb(60, 66, 76)))
            g.FillRectangle(paper, 0, 0, (WD + 2 * PADX) * z, (H + 2 * PADY) * z);
        g.TranslateTransform(PADX * z, PADY * z);
        g.FillRectangle(Brushes.White, 0, 0, WD * z, H * z);
        using (Pen grid = new Pen(Color.FromArgb(228, 232, 237)))
        {
            for (int mm = 5; mm * MM < WD; mm += 5) { float x = mm * MM * z + 0.5f; g.DrawLine(grid, x, 0, x, H * z); }
            for (int mm = 5; mm * MM < H; mm += 5) { float y = mm * MM * z + 0.5f; g.DrawLine(grid, 0, y, WD * z, y); }
        }
        DrawElements(g, zoom);
        if (sel != null)
        {
            Measure(sel);
            GraphicsState st = g.Save();
            g.TranslateTransform((float)((sel.x + sel.w / 2) * MM * z), (float)((sel.y + sel.h / 2) * MM * z));
            if (sel.rot != 0) g.RotateTransform((float)sel.rot);
            using (Pen p = new Pen(Color.FromArgb(62, 166, 255)))
            {
                p.DashStyle = DashStyle.Dash;
                g.DrawRectangle(p, (float)(-sel.w / 2 * MM * z) - 0.5f, (float)(-sel.h / 2 * MM * z) - 0.5f, (float)(sel.w * MM * z) + 1, (float)(sel.h * MM * z) + 1);
                g.DrawLine(p, 0, (float)(-sel.h / 2 * MM * z), 0, (float)(-sel.h / 2 * MM * z) - 22);
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(62, 166, 255)))
            {
                float hw = (float)(sel.w / 2 * MM * z), hh = (float)(sel.h / 2 * MM * z);
                int[][] cs = new int[][] { new int[] { -1, -1 }, new int[] { 1, -1 }, new int[] { -1, 1 }, new int[] { 1, 1 } };
                foreach (int[] c in cs) g.FillRectangle(b, c[0] * hw - 3, c[1] * hh - 3, 6, 6);
                g.FillEllipse(b, -5, (float)(-sel.h / 2 * MM * z) - 27, 10, 10);
            }
            g.Restore(st);
        }
        using (Pen edge = new Pen(Color.FromArgb(120, 128, 140)))
        {
            edge.DashStyle = DashStyle.Dot;
            g.DrawRectangle(edge, -0.5f, -0.5f, WD * z - 1, H * z - 1);
        }
    }

    // ---------------- maus und tastatur ----------------

    int mode;
    int startPx, startPy;
    double ex0, ey0, rot0, cx, cy;

    Elem Hit(Point p)
    {
        for (int i = els.Count - 1; i >= 0; i--)
        {
            Elem e = els[i];
            Measure(e);
            double ecx = (e.x + e.w / 2) * MM, ecy = (e.y + e.h / 2) * MM;
            double dx = p.X - ecx, dy = p.Y - ecy;
            double a = -e.rot * Math.PI / 180;
            double ux = dx * Math.Cos(a) - dy * Math.Sin(a);
            double uy = dx * Math.Sin(a) + dy * Math.Cos(a);
            if (Math.Abs(ux) <= e.w / 2 * MM + 2 && Math.Abs(uy) <= e.h / 2 * MM + 2) return e;
            if (Math.Sqrt(dx * dx + (dy - e.h / 2 * MM - 26) * (dy - e.h / 2 * MM - 26)) < 9) return e;
        }
        return null;
    }

    void OnDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Point p = new Point((int)(e.X / zoom) - PADX, (int)(e.Y / zoom) - PADY);
        Elem h = Hit(p);
        if (h == null) { Select(null); return; }
        Select(h);
        Measure(h);
        cx = (h.x + h.w / 2) * MM; cy = (h.y + h.h / 2) * MM;
        double dx = p.X - cx, dy = p.Y - cy - h.h / 2 * MM - 26;
        mode = Math.Sqrt(dx * dx + dy * dy) < 10 ? 2 : 1;
        startPx = e.X; startPy = e.Y; ex0 = h.x; ey0 = h.y; rot0 = h.rot;
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (mode == 0 || sel == null) return;
        if (mode == 1)
        {
            double nx = ex0 + (e.X - startPx) / zoom / MM;
            double ny = ey0 + (e.Y - startPy) / zoom / MM;
            if (snap > 0 && (Control.ModifierKeys & Keys.Shift) == 0)
            {
                nx = Math.Round(nx / snap) * snap;
                ny = Math.Round(ny / snap) * snap;
            }
            sel.x = Math.Round(nx * 10) / 10; sel.y = Math.Round(ny * 10) / 10;
            loading = true;
            numX.Value = Clamp(numX, (decimal)sel.x); numY.Value = Clamp(numY, (decimal)sel.y);
            loading = false;
        }
        else
        {
            Point p = new Point((int)(e.X / zoom) - PADX, (int)(e.Y / zoom) - PADY);
            double ang = Math.Atan2(p.Y - cy, p.X - cx) * 180 / Math.PI + 90;
            sel.rot = Math.Round(ang) % 360;
            loading = true;
            trRot.Value = (int)((sel.rot % 360 + 360) % 360);
            loading = false;
            lbRot.Text = ((int)sel.rot).ToString();
        }
        canvas.Invalidate(); UpdateInfo();
    }

    void OnUp(object sender, MouseEventArgs e) { mode = 0; }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (sel == null) return;
        double s = (e.Modifiers & Keys.Shift) != 0 ? 0.1 : (snap > 0 ? snap : 0.5);
        if (e.KeyCode == Keys.Left) sel.x -= s;
        else if (e.KeyCode == Keys.Right) sel.x += s;
        else if (e.KeyCode == Keys.Up) sel.y -= s;
        else if (e.KeyCode == Keys.Down) sel.y += s;
        else if (e.KeyCode == Keys.Delete) { DeleteSel(); e.Handled = true; return; }
        else if (e.Control && e.KeyCode == Keys.D) { Duplicate(); e.Handled = true; return; }
        else return;
        sel.x = Math.Round(sel.x * 10) / 10; sel.y = Math.Round(sel.y * 10) / 10;
        loading = true;
        numX.Value = Clamp(numX, (decimal)sel.x); numY.Value = Clamp(numY, (decimal)sel.y);
        loading = false;
        canvas.Invalidate(); UpdateInfo();
        e.Handled = true;
    }

    // ---------------- rastern und drucken ----------------

    byte[] BuildPacked(out int wb, out int h)
    {
        int H = LabelDots();
        Bitmap bmp = new Bitmap(WD, H);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            DrawElements(g, 1);
        }
        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, WD, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int[] px = new int[WD * H];
        System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, px, 0, px.Length);
        bmp.UnlockBits(bd);
        bmp.Dispose();
        double[] gray = new double[WD * H];
        for (int i = 0; i < gray.Length; i++)
        {
            double y = 0.299 * ((px[i] >> 16) & 0xFF) + 0.587 * ((px[i] >> 8) & 0xFF) + 0.114 * (px[i] & 0xFF);
            y = (y - 128) * contrast + 128;
            if (chInvert.Checked) y = 255 - y;
            gray[i] = y < 0 ? 0 : (y > 255 ? 255 : y);
        }
        wb = WD / 8; h = H;
        byte[] outp = new byte[wb * H];
        for (int i = 0; i < outp.Length; i++) outp[i] = 0xFF;
        if (!chDither.Checked)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < WD; x++)
                    if (gray[y * WD + x] < 128) outp[y * wb + (x >> 3)] &= (byte)~(1 << (7 - (x & 7)));
        }
        else
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < WD; x++)
                {
                    int i = y * WD + x;
                    double old = gray[i];
                    double nv = old < 128 ? 0 : 255;
                    double err = old - nv;
                    gray[i] = nv;
                    if (nv == 0) outp[y * wb + (x >> 3)] &= (byte)~(1 << (7 - (x & 7)));
                    if (x + 1 < WD) gray[i + 1] += err * 7 / 16;
                    if (y + 1 < H)
                    {
                        if (x > 0) gray[i + WD - 1] += err * 3 / 16;
                        gray[i + WD] += err * 5 / 16;
                        if (x + 1 < WD) gray[i + WD + 1] += err / 16;
                    }
                }
        }
        return outp;
    }

    void DoPrint()
    {
        if (els.Count == 0) { MessageBox.Show(fm, "Kein Inhalt – füge zuerst Text oder ein Bild ein.", "PM230"); return; }
        int wb, h;
        byte[] packed = BuildPacked(out wb, out h);
        byte[] job = Tspl.Job(packed, wb, h, density, 54);
        List<string> ports = FindPorts();
        if (ports.Count == 0)
        {
            MessageBox.Show(fm,
                "Der PM230 ist nicht erreichbar.\r\n\r\n" +
                "– Drucker einschalten (Power-Taste gedrückt halten)\r\n" +
                "– in Windows: Einstellungen > Bluetooth > PM230 verbinden\r\n" +
                "– Handy-App trennen: der Drucker kann nur eine Verbindung",
                "PM230 nicht gefunden", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        btnPrint.Enabled = false;
        stStatus.Text = "drucke " + (h / (double)MM).ToString("0.#") + " mm Label auf " + ports[0] + " ...";
        fm.Cursor = Cursors.WaitCursor;
        try
        {
            using (IPrintLink l = new SerialLink(ports[0]))
            {
                linkName = l.Name;
                byte[] buf = new byte[512];
                Query(l, Tspl.QueryStatus, buf);
                StringBuilder heard = new StringBuilder();
                for (int i = 0; i < copies; i++)
                {
                    l.Write(job);
                    DateTime until = DateTime.Now.AddMilliseconds(7000);
                    while (DateTime.Now < until)
                    {
                        int n = l.Read(buf, 300);
                        if (n > 0) heard.Append(HexS(buf, n));
                    }
                }
                stStatus.Text = "fertig  ·  Drucker antwortet: " + (heard.Length > 0 ? heard.ToString() : "ok") +
                    "  ·  " + job.Length + " Bytes";
            }
        }
        catch (Exception ex)
        {
            stStatus.Text = "Drucken fehlgeschlagen";
            MessageBox.Show(fm, "Drucken fehlgeschlagen:\r\n" + ex.Message, "PM230", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        fm.Cursor = Cursors.Default;
        btnPrint.Enabled = true;
        RefreshStatus();
    }

    static int Query(IPrintLink l, byte[] q, byte[] buf)
    {
        l.Write(q);
        byte[] tmp = new byte[128];
        int n = 0;
        DateTime until = DateTime.Now.AddMilliseconds(2500);
        while (DateTime.Now < until && n == 0)
        {
            int k = l.Read(tmp, 400);
            if (k > 0) { n = Math.Min(k, buf.Length); Array.Copy(tmp, 0, buf, 0, n); }
        }
        return n;
    }

    static string HexS(byte[] b, int n)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append("[" + b[i].ToString("X2") + "]");
        return sb.ToString();
    }

    static List<string> FindPorts()
    {
        List<string> outp = new List<string>();
        string macHex = null;
        try
        {
            using (System.Management.ManagementObjectSearcher s = new System.Management.ManagementObjectSearcher(
                "SELECT DeviceID, Name FROM Win32_PnPEntity WHERE DeviceID LIKE 'BTHENUM%'"))
            {
                foreach (System.Management.ManagementObject o in s.Get())
                {
                    string nm = o["Name"] as string, id = o["DeviceID"] as string;
                    if (nm != null && id != null && nm.IndexOf(DEVNAME, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int i = id.IndexOf("DEV_");
                        if (i >= 0 && id.Length >= i + 16) { macHex = id.Substring(i + 4, 12); break; }
                    }
                }
            }
            if (macHex == null) return outp;
            using (System.Management.ManagementObjectSearcher s2 = new System.Management.ManagementObjectSearcher(
                "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE PNPClass='Ports' AND DeviceID LIKE '%" + macHex + "%'"))
            {
                foreach (System.Management.ManagementObject o in s2.Get())
                {
                    string nm = o["Name"] as string;
                    if (nm == null) continue;
                    int a = nm.IndexOf("(COM"), b = a >= 0 ? nm.IndexOf(')', a) : -1;
                    if (a >= 0 && b > a) outp.Add(nm.Substring(a + 1, b - a - 1));
                }
            }
        }
        catch (Exception) { }
        return outp;
    }

    void RefreshStatus()
    {
        Thread th = new Thread(delegate()
        {
            string batt = "?", status = "nicht erreichbar";
            List<string> ports = FindPorts();
            if (ports.Count > 0)
            {
                try
                {
                    using (IPrintLink l = new SerialLink(ports[0]))
                    {
                        linkName = l.Name;
                        byte[] buf = new byte[256];
                        int n = Query(l, Tspl.QueryBattery, buf);
                        int at = -1;
                        byte[] key = Encoding.ASCII.GetBytes("BATTERY ");
                        for (int i = 0; i + key.Length < n; i++)
                        {
                            bool ok = true;
                            for (int j = 0; j < key.Length; j++) if (buf[i + j] != key[j]) { ok = false; break; }
                            if (ok) { at = i; break; }
                        }
                        if (at >= 0 && n > at + key.Length)
                        {
                            int raw = buf[at + key.Length];
                            int bcd = ((raw >> 4) & 0x0F) * 10 + (raw & 0x0F);
                            batt = (bcd > 100 ? raw : bcd) + " %";
                        }
                        Array.Clear(buf, 0, buf.Length);
                        n = Query(l, Tspl.QueryStatus, buf);
                        if (n == 1) status = Tspl.StatusText(buf[0]);
                        else if (n > 0) status = HexS(buf, n);
                        else status = "antwortet nicht";
                    }
                }
                catch (Exception) { status = "Port offen, aber keine Antwort"; }
            }
            string txt2 = "Akku " + batt + "   ·   " + status;
            string long2 = status + "  ·  " + linkName;
            bool good = status == "ready";
            try
            {
                fm.BeginInvoke((MethodInvoker)delegate
                {
                    lbBattery.Text = txt2;
                    if (stStatus != null) stStatus.Text = long2;
                    lbBattery.ForeColor = good ? Color.FromArgb(56, 211, 159) : Color.FromArgb(255, 138, 138);
                    UpdateInfo();
                });
            }
            catch (Exception) { }
        });
        th.IsBackground = true;
        th.Start();
    }

    Bitmap[] RenderPdf(string file)
    {
        string dir = Path.GetDirectoryName(Application.ExecutablePath);
        string helper = Path.Combine(dir, "pdf2png.ps1");
        if (!File.Exists(helper))
        {
            MessageBox.Show(fm, "pdf2png.ps1 fehlt im Ordner der Programmdatei – ohne sie ist kein PDF-Import möglich.", "PM230");
            return new Bitmap[0];
        }
        string tmp = Path.Combine(Path.GetTempPath(), "pm230");
        Directory.CreateDirectory(tmp);
        foreach (string f in Directory.GetFiles(tmp, "page-*.png")) { try { File.Delete(f); } catch (Exception) { } }
        string src = Path.Combine(tmp, "input.pdf");
        try { File.Copy(file, src, true); }
        catch (Exception ex) { MessageBox.Show(fm, "PDF konnte nicht gelesen werden: " + ex.Message, "PM230"); return new Bitmap[0]; }
        System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
            "powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + helper + "\" -In \"" + src + "\" -OutDir \"" + tmp + "\"");
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        try
        {
            using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(90000);
            }
        }
        catch (Exception ex) { MessageBox.Show(fm, "PDF-Renderer konnte nicht gestartet werden: " + ex.Message, "PM230"); return new Bitmap[0]; }
        string[] files = Directory.GetFiles(tmp, "page-*.png");
        Array.Sort(files);
        Bitmap[] outp = new Bitmap[files.Length];
        for (int i = 0; i < files.Length; i++) using (Bitmap t = new Bitmap(files[i])) outp[i] = new Bitmap(t);
        return outp;
    }
}

class DarkTable : ProfessionalColorTable
{
    static readonly Color flat = Color.FromArgb(24, 28, 35);
    static readonly Color line = Color.FromArgb(51, 59, 73);
    public override Color ToolStripGradientBegin { get { return flat; } }
    public override Color ToolStripGradientMiddle { get { return flat; } }
    public override Color ToolStripGradientEnd { get { return flat; } }
    public override Color ToolStripBorder { get { return line; } }
    public override Color StatusStripGradientBegin { get { return flat; } }
    public override Color StatusStripGradientEnd { get { return flat; } }
    public override Color SeparatorDark { get { return line; } }
    public override Color SeparatorLight { get { return line; } }
}

class DoublePanel : Panel
{
    public DoublePanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
}
