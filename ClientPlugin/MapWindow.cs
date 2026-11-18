using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SDX2_SystemMap;

/// <summary>One input event from the second screen's window, for the game thread.</summary>
internal struct WinInput
{
    public enum K : byte { TouchDown, TouchMove, TouchUp, TouchLost, MouseMove, MouseDown, MouseUp, Wheel }
    public K Kind;
    public int Id;          // touch: the pointer id; mouse: 0 left, 1 right
    public float X, Y;      // window pixels
    public int Delta;       // wheel
}

/// <summary>
/// The second screen: a plain window on its own thread that plays back the frames the game thread records
/// (<see cref="DrawList"/>) and passes touch, pen and mouse input back. It never takes the focus from the game
/// (WS_EX_NOACTIVATE): touching it leaves your keyboard and mouse in the cockpit. Touch comes as Windows pointer
/// messages, so Windows makes no mouse clicks or press-and-hold right-clicks out of it.
/// On another monitor it fills it without a border; on the only one it's a normal window (its WINDOW button switches).
/// </summary>
internal sealed class MapWindow : Form
{
    public readonly ConcurrentQueue<WinInput> Inputs = new ConcurrentQueue<WinInput>();
    private readonly ConcurrentQueue<DrawList> free = new ConcurrentQueue<DrawList>();
    private DrawList ready, shown;
    private readonly GdiCanvas canvas = new GdiCanvas();
    private readonly System.Windows.Forms.Timer timer;
    private volatile int width, height;
    private volatile bool gone;
    private readonly Rectangle screen;
    private readonly bool ownScreen;

    /// <summary>The window's drawing area (px), for the game thread.</summary>
    public int ClientW => width;
    public int ClientH => height;
    /// <summary>Closed (by you, or Windows): the game thread lets it go.</summary>
    public bool Gone => gone;
    /// <summary>Shift held (read by the game thread for Shift+click with a mouse on this screen).</summary>
    public static bool ShiftHeld => (Control.ModifierKeys & Keys.Shift) != 0;

    public MapWindow(Rectangle screen, bool ownScreen)
    {
        this.screen = screen; this.ownScreen = ownScreen;
        for (int i = 0; i < 3; i++) free.Enqueue(new DrawList());
        Text = "SDX2 System Map";
        BackColor = Color.Black;
        StartPosition = FormStartPosition.Manual;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Fill(ownScreen);
        timer = new System.Windows.Forms.Timer { Interval = 15 };
        timer.Tick += (s, e) => { if (Volatile.Read(ref ready) != null) Invalidate(); };
        timer.Start();
    }

    private void Fill(bool full)
    {
        ownFull = full;
        if (full) { FormBorderStyle = FormBorderStyle.None; Bounds = screen; }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            int w = Math.Min(1280, screen.Width - 80), h = Math.Min(800, screen.Height - 80);
            Bounds = new Rectangle(screen.X + (screen.Width - w) / 2, screen.Y + (screen.Height - h) / 2, w, h);
        }
        Sizes();
    }

    private void Sizes() { width = Math.Max(1, ClientSize.Width); height = Math.Max(1, ClientSize.Height); }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Sizes(); }

    protected override bool ShowWithoutActivation => true;

    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_APPWINDOW = 0x00040000;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= WS_EX_NOACTIVATE | WS_EX_APPWINDOW; return p; }
    }

    [DllImport("user32.dll")] private static extern bool GetPointerType(uint pointerId, out int pointerType);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(IntPtr hWnd, string lpString, IntPtr hData);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // no press-and-hold ring, flicks or tap feedback from the pen / touch service
        try { SetProp(Handle, "MicrosoftTabletPenServiceProperty", (IntPtr)0x00010019); } catch { }
    }

    private const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3, WM_POINTERACTIVATE = 0x024B, PA_NOACTIVATE = 3;
    private const int WM_POINTERUPDATE = 0x0245, WM_POINTERDOWN = 0x0246, WM_POINTERUP = 0x0247, WM_POINTERCAPTURECHANGED = 0x024C;
    private const int PT_TOUCH = 2, PT_PEN = 3;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE: m.Result = (IntPtr)MA_NOACTIVATE; return;
            case WM_POINTERACTIVATE: m.Result = (IntPtr)PA_NOACTIVATE; return;
            case WM_POINTERDOWN: case WM_POINTERUPDATE: case WM_POINTERUP: case WM_POINTERCAPTURECHANGED:
                uint id = (uint)((long)m.WParam & 0xFFFF);
                if (GetPointerType(id, out int type) && (type == PT_TOUCH || type == PT_PEN))
                {
                    long lp = (long)m.LParam;
                    var at = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    var kind = m.Msg == WM_POINTERDOWN ? WinInput.K.TouchDown : m.Msg == WM_POINTERUPDATE ? WinInput.K.TouchMove
                        : m.Msg == WM_POINTERUP ? WinInput.K.TouchUp : WinInput.K.TouchLost;
                    Inputs.Enqueue(new WinInput { Kind = kind, Id = (int)id, X = at.X, Y = at.Y });
                    m.Result = IntPtr.Zero;
                    return; // handled: Windows makes no mouse messages out of it
                }
                break;
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Inputs.Enqueue(new WinInput { Kind = WinInput.K.MouseMove, X = e.X, Y = e.Y }); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Button(WinInput.K.MouseDown, e); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Button(WinInput.K.MouseUp, e); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Inputs.Enqueue(new WinInput { Kind = WinInput.K.Wheel, X = e.X, Y = e.Y, Delta = e.Delta }); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Inputs.Enqueue(new WinInput { Kind = WinInput.K.MouseMove, X = float.NaN, Y = float.NaN }); }

    private void Button(WinInput.K kind, MouseEventArgs e)
    {
        int b = e.Button == MouseButtons.Left ? 0 : e.Button == MouseButtons.Right ? 1 : -1;
        if (b >= 0) Inputs.Enqueue(new WinInput { Kind = kind, Id = b, X = e.X, Y = e.Y });
    }

    /// <summary>Fills its screen without a border (true) or stands as a normal window; from any thread.</summary>
    public void SetFull(bool full)
    {
        try { if (IsHandleCreated && !gone) BeginInvoke(new Action(() => Fill(full))); } catch { }
    }

    /// <summary>Filling its screen without a border.</summary>
    public bool Full => ownFull;
    private volatile bool ownFull;

    /// <summary>Game thread: a list to record the next frame into, or null while the window is still behind.</summary>
    public DrawList TakeFree() => free.TryDequeue(out var l) ? l : null;

    /// <summary>Game thread: the frame is recorded; the window shows it next.</summary>
    public void Publish(DrawList l)
    {
        var old = Interlocked.Exchange(ref ready, l);
        if (old != null) free.Enqueue(old);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var next = Interlocked.Exchange(ref ready, null);
        if (next != null) { if (shown != null) free.Enqueue(shown); shown = next; }
        var g = e.Graphics;
        g.Clear(Color.Black);
        if (shown == null || shown.Count == 0)
        {
            using var f = new Font("Segoe UI", 16, GraphicsUnit.Pixel);
            g.DrawString("SDX2 System Map: waiting for the game...", f, Brushes.Gray, 20, 20);
            return;
        }
        try { canvas.Bind(g, ClientSize.Width, ClientSize.Height); shown.Replay(canvas); }
        catch (Exception ex) { Plugin.LogThrottled("second screen paint failed: " + ex.Message); }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        gone = true;
        timer.Stop();
        canvas.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>From any thread.</summary>
    public void CloseSoon()
    {
        gone = true;
        try { if (IsHandleCreated) BeginInvoke(new Action(Close)); } catch { }
    }

    /// <summary>Starts a window on its own thread (its message loop); returns it once its handle exists, or null.</summary>
    public static MapWindow Start(Rectangle screen, bool ownScreen)
    {
        MapWindow w = null;
        var made = new ManualResetEventSlim(); // not disposed: the window's thread may still set it
        var t = new Thread(() =>
        {
            try
            {
                w = new MapWindow(screen, ownScreen);
                w.HandleCreated += (s, e) => made.Set();
                w.Show();
                made.Set();
                Application.Run(w);
            }
            catch (Exception e) { Plugin.Log("second screen failed: " + e); made.Set(); }
        }) { IsBackground = true, Name = "SDX2 map second screen" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        made.Wait(5000);
        return w != null && !w.gone ? w : null;
    }
}
