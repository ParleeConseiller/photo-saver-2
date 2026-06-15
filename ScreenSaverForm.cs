using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System.Runtime.InteropServices;

namespace PhotoSaverAnimated;

public sealed class ScreenSaverForm : Form
{
    #region Win32 P/Invoke (preview mode)
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
    [DllImport("user32.dll")] static extern int    SetWindowLong(IntPtr hwnd, int index, IntPtr newLong);
    [DllImport("user32.dll")] static extern int    GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool   GetClientRect(IntPtr hwnd, out W32Rect rect);
    [StructLayout(LayoutKind.Sequential)]
    private struct W32Rect { public int Left, Top, Right, Bottom; }
    private const int GWL_STYLE = -16;
    private const int WS_CHILD  = 0x40000000;
    #endregion

    private const float PolaroidBorderSide   = 14f;
    private const float PolaroidBorderTop    = 14f;
    private const float PolaroidBorderBottom = 52f;
    private const float MaxPhotoFrac = 0.38f;
    private const float ShadowOff    = 9f;
    private const byte  ShadowAlpha  = 90;
    private const int   TickMs       = 16;

    private static readonly SKColor BgColor = new(18, 18, 18);

    private static readonly HashSet<string> ImageExts =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    private static readonly HashSet<string> VideoExts =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".avi", ".mov", ".mkv", ".wmv", ".m4v", ".flv" };

    private readonly Random _rng = new();

    private sealed class MediaEntry
    {
        public MediaKind                Kind      { get; init; }
        public IReadOnlyList<AnimFrame> Frames    { get; init; } = Array.Empty<AnimFrame>();
        public string?                  VideoPath { get; init; }
        public int                      Width     { get; init; }
        public int                      Height    { get; init; }
    }

    private readonly List<MediaEntry> _media = new();
    private readonly List<PhotoCard>  _cards = new();
    private readonly SKGLControl      _gl;
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly System.Windows.Forms.Timer _launchTimer;

    private readonly bool   _preview;
    private readonly IntPtr _previewHwnd;
    private Screen _targetScreen = Screen.PrimaryScreen!;

    public event Action? ExitRequested;

    private Point    _lastMouse;
    private bool     _mouseTracked;
    private int      _nextIdx;
    private DateTime _startTime;
    private DateTime _lastTickTime;
    private bool     _loadingComplete;
    private string?  _errorMessage;

    private enum Stage { Loading, Running, Clearing }
    private Stage    _stage = Stage.Loading;
    private DateTime _clearStart;
    private const double ClearSecs = 1.5;

    private bool             _rollingMode;
    private CardBorderStyle  _borderStyle;
    private CardOrientation  _cardOrientation;

    private List<AnimFrame>? _bgFrames;
    private VideoPlayer?     _bgVideo;
    private MediaKind        _bgKind;
    private int              _bgFrameIdx;
    private double           _bgFrameElapsedMs;
    private bool             _hasBackground;
    private BackgroundFit    _bgFit;

    private bool ReadyToExit => !_preview && (DateTime.Now - _startTime).TotalSeconds > 1.0;

    // ── Constructors ──────────────────────────────────────────────────────────

    public ScreenSaverForm(Screen screen) : this(IntPtr.Zero) { _targetScreen = screen; }

    public ScreenSaverForm(IntPtr previewHwnd = default)
    {
        _preview     = previewHwnd != IntPtr.Zero;
        _previewHwnd = previewHwnd;

        BackColor  = Color.FromArgb(18, 18, 18);
        KeyPreview = true;

        if (!_preview) { FormBorderStyle = FormBorderStyle.None; TopMost = true; }

        _gl = new SKGLControl { Dock = DockStyle.Fill, BackColor = Color.Black };
        _gl.PaintSurface += OnPaintSurface;
        _gl.MouseMove    += (_, e) => HandleMouseMove(e.Location);
        _gl.MouseClick   += (_, _) => { if (ReadyToExit) RequestExit(); };
        _gl.KeyDown      += (_, _) => { if (ReadyToExit) RequestExit(); };
        Controls.Add(_gl);

        _renderTimer = new System.Windows.Forms.Timer { Interval = TickMs };
        _renderTimer.Tick += OnTick;

        _launchTimer = new System.Windows.Forms.Timer();
        _launchTimer.Tick += (_, _) => LaunchNext();
    }

    private void RequestExit()
    {
        BeginInvoke(new MethodInvoker(() =>
        {
            if (ExitRequested != null) ExitRequested.Invoke();
            else Close();
        }));
    }

    // ── Form overrides ────────────────────────────────────────────────────────

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (_preview && _previewHwnd != IntPtr.Zero)
        {
            GetClientRect(_previewHwnd, out var r);
            SetParent(Handle, _previewHwnd);
            SetWindowLong(Handle, GWL_STYLE, (IntPtr)(GetWindowLong(Handle, GWL_STYLE) | WS_CHILD));
            Bounds = new Rectangle(0, 0, r.Right - r.Left, r.Bottom - r.Top);
        }
        else if (!_preview) { Bounds = _targetScreen.Bounds; }

        if (!_preview) Cursor.Hide();
        _startTime    = DateTime.Now;
        _lastTickTime = DateTime.Now;
        _rollingMode      = AppSettings.RollingMode;
        _borderStyle      = AppSettings.CardBorder;
        _bgFit            = AppSettings.BackgroundFitMode;
        _cardOrientation  = AppSettings.CardOrientationMode;
        _launchTimer.Interval = Math.Max(500, AppSettings.LaunchIntervalSeconds * 1000);
        _renderTimer.Start();
        _ = LoadMediaAsync();
        _ = LoadBackgroundAsync();
    }

    protected override void OnMouseMove(MouseEventArgs e)  { base.OnMouseMove(e);  HandleMouseMove(e.Location); }
    protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); if (ReadyToExit) RequestExit(); }
    protected override void OnKeyDown(KeyEventArgs e)      { base.OnKeyDown(e);    if (ReadyToExit) RequestExit(); }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _renderTimer.Stop();
        _launchTimer.Stop();
        if (!_preview) Cursor.Show();
        foreach (var c in _cards) c.Dispose();
        _cards.Clear();
        foreach (var m in _media)
            foreach (var f in m.Frames)
                f.Bitmap.Dispose();
        _media.Clear();
        _bgVideo?.Dispose();
        if (_bgFrames != null) { foreach (var f in _bgFrames) f.Bitmap.Dispose(); _bgFrames = null; }
        base.OnFormClosed(e);
    }

    // ── Media loading ─────────────────────────────────────────────────────────

    private async Task LoadMediaAsync()
    {
        try
        {
            var folder = AppSettings.PhotoFolder;
            string[] files = Array.Empty<string>();

            if (Directory.Exists(folder))
            {
                files = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => ImageExts.Contains(Path.GetExtension(f))
                             || VideoExts.Contains(Path.GetExtension(f)))
                    .OrderBy(_ => _rng.Next())
                    .ToArray();
            }

            int maxPx = (int)(Math.Min(ClientSize.Width, ClientSize.Height) * MaxPhotoFrac);
            maxPx = Math.Max(maxPx, 80);

            foreach (var path in files)
            {
                if (IsDisposed) return;

                bool isVideo = VideoExts.Contains(Path.GetExtension(path));
                MediaEntry? entry = isVideo
                    ? await Task.Run(() => TryMakeVideoEntry(path, maxPx))
                    : await Task.Run(() => TryMakeImageEntry(path, maxPx));

                if (entry == null) continue;

                if (entry.Kind != MediaKind.Video && _cardOrientation != CardOrientation.Natural)
                {
                    bool isLandscape = entry.Width > entry.Height;
                    bool isPortrait  = entry.Height > entry.Width;
                    bool needsRotate = (_cardOrientation == CardOrientation.Landscape && isPortrait)
                                   || (_cardOrientation == CardOrientation.Portrait  && isLandscape);
                    if (needsRotate) entry = RotateEntry90(entry);
                }

                _media.Add(entry);

                if (_stage == Stage.Loading)
                {
                    _stage = Stage.Running;
                    LaunchNext();
                    _launchTimer.Start();
                }
            }

            _loadingComplete = true;
            if (_stage == Stage.Loading) _stage = Stage.Running;
        }
        catch (Exception ex)
        {
            _loadingComplete = true;
            _stage           = Stage.Running;
            _errorMessage    = ex.Message;
        }
    }

    private async Task LoadBackgroundAsync()
    {
        var path = AppSettings.BackgroundFile;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var ext = Path.GetExtension(path);
        int sw  = ClientSize.Width;
        int sh  = ClientSize.Height;

        if (VideoExts.Contains(ext))
        {
            _bgVideo       = await Task.Run(() => new VideoPlayer(path, sw, sh));
            _bgKind        = MediaKind.Video;
            _hasBackground = true;
        }
        else if (ImageExts.Contains(ext))
        {
            int maxDim = Math.Max(sw, sh);
            var entry  = await Task.Run(() => TryMakeImageEntry(path, maxDim));
            if (entry == null || IsDisposed) return;
            _bgFrames      = entry.Frames.ToList();
            _bgKind        = entry.Kind;
            _hasBackground = true;
        }
    }

    private static MediaEntry? TryMakeImageEntry(string path, int maxPx)
    {
        try
        {
            using var data  = SKData.Create(path);
            if (data == null) return null;
            using var codec = SKCodec.Create(data);
            if (codec == null) return null;

            if (codec.FrameCount > 1)
            {
                var frames = DecodeAnimatedFrames(codec, maxPx);
                if (frames == null || frames.Count == 0) return null;
                return new MediaEntry { Kind = MediaKind.Animated, Frames = frames,
                    Width = frames[0].Bitmap.Width, Height = frames[0].Bitmap.Height };
            }

            var origin  = codec.EncodedOrigin;
            using var decoded = SKBitmap.Decode(data);
            if (decoded == null) return null;

            SKBitmap? corrected = origin != SKEncodedOrigin.TopLeft
                ? ApplyExifOrientation(decoded, origin) : null;
            var src = corrected ?? decoded;
            try
            {
                float s = Math.Min((float)maxPx / src.Width, (float)maxPx / src.Height);
                int w = Math.Max(1, (int)(src.Width  * s));
                int h = Math.Max(1, (int)(src.Height * s));
                var bmp   = src.Resize(new SKImageInfo(w, h), SKFilterQuality.High);
                var frame = new AnimFrame { Bitmap = bmp, DurationMs = 0 };
                return new MediaEntry { Kind = MediaKind.Still, Frames = new[] { frame }, Width = w, Height = h };
            }
            finally { corrected?.Dispose(); }
        }
        catch { return null; }
    }

    private static List<AnimFrame>? DecodeAnimatedFrames(SKCodec codec, int maxPx)
    {
        int frameCount = codec.FrameCount;
        var frameInfos = codec.FrameInfo;
        var fullInfo   = new SKImageInfo(codec.Info.Width, codec.Info.Height,
                                         SKColorType.Bgra8888, SKAlphaType.Premul);
        var fullRes = new SKBitmap[frameCount];

        for (int fi = 0; fi < frameCount; fi++)
        {
            var bmp      = new SKBitmap(fullInfo);
            int required = (frameInfos != null && fi < frameInfos.Length)
                ? frameInfos[fi].RequiredFrame : -1;

            if (required >= 0 && fullRes[required] != null)
                fullRes[required].CopyTo(bmp);

            codec.GetPixels(fullInfo, bmp.GetPixels(), new SKCodecOptions(fi, required));
            fullRes[fi] = bmp;
        }

        float s   = Math.Min(1f, Math.Min((float)maxPx / fullInfo.Width, (float)maxPx / fullInfo.Height));
        int   outW = Math.Max(1, (int)(fullInfo.Width  * s));
        int   outH = Math.Max(1, (int)(fullInfo.Height * s));

        var frames = new List<AnimFrame>(frameCount);
        for (int fi = 0; fi < frameCount; fi++)
        {
            SKBitmap display;
            if (s < 1f) { display = fullRes[fi].Resize(new SKImageInfo(outW, outH), SKFilterQuality.Medium); fullRes[fi].Dispose(); }
            else           display = fullRes[fi];

            int dur = (frameInfos != null && fi < frameInfos.Length)
                ? Math.Max(10, frameInfos[fi].Duration) : 100;
            frames.Add(new AnimFrame { Bitmap = display, DurationMs = dur });
        }
        return frames;
    }

    private static SKBitmap ApplyExifOrientation(SKBitmap src, SKEncodedOrigin origin)
    {
        bool swapDims = origin is SKEncodedOrigin.RightTop  or SKEncodedOrigin.LeftBottom
                                or SKEncodedOrigin.LeftTop  or SKEncodedOrigin.RightBottom;
        int outW = swapDims ? src.Height : src.Width;
        int outH = swapDims ? src.Width  : src.Height;
        var dst  = new SKBitmap(outW, outH, src.ColorType, src.AlphaType);
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Empty);
        var mx = origin switch
        {
            SKEncodedOrigin.TopRight    => new SKMatrix { ScaleX=-1, SkewX=0,  TransX=src.Width-1,  SkewY=0,  ScaleY=1,  TransY=0,            Persp2=1 },
            SKEncodedOrigin.BottomRight => new SKMatrix { ScaleX=-1, SkewX=0,  TransX=src.Width-1,  SkewY=0,  ScaleY=-1, TransY=src.Height-1, Persp2=1 },
            SKEncodedOrigin.BottomLeft  => new SKMatrix { ScaleX=1,  SkewX=0,  TransX=0,            SkewY=0,  ScaleY=-1, TransY=src.Height-1, Persp2=1 },
            SKEncodedOrigin.LeftTop     => new SKMatrix { ScaleX=0,  SkewX=1,  TransX=0,            SkewY=1,  ScaleY=0,  TransY=0,            Persp2=1 },
            SKEncodedOrigin.RightTop    => new SKMatrix { ScaleX=0,  SkewX=-1, TransX=src.Height-1, SkewY=1,  ScaleY=0,  TransY=0,            Persp2=1 },
            SKEncodedOrigin.RightBottom => new SKMatrix { ScaleX=0,  SkewX=-1, TransX=src.Height-1, SkewY=-1, ScaleY=0,  TransY=src.Width-1,  Persp2=1 },
            SKEncodedOrigin.LeftBottom  => new SKMatrix { ScaleX=0,  SkewX=1,  TransX=0,            SkewY=-1, ScaleY=0,  TransY=src.Width-1,  Persp2=1 },
            _ => SKMatrix.Identity,
        };
        canvas.SetMatrix(mx);
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }

    private static MediaEntry? TryMakeVideoEntry(string path, int maxPx)
    {
        int w = maxPx;
        int h = Math.Max(1, (int)(maxPx * 9.0 / 16.0));
        return new MediaEntry { Kind = MediaKind.Video, VideoPath = path, Width = w, Height = h };
    }

    // ── Animation state machine ───────────────────────────────────────────────

    private void LaunchNext()
    {
        if (_stage != Stage.Running || _media.Count == 0) return;

        if (_rollingMode && _nextIdx >= _media.Count)
        {
            _nextIdx = 0;
            var shuffled = _media.OrderBy(_ => _rng.Next()).ToList();
            _media.Clear(); _media.AddRange(shuffled);
        }

        if (_nextIdx >= _media.Count) { if (_loadingComplete) BeginClear(); return; }

        int active = _cards.Count(c => c.State != CardState.FadingOut);
        if (active >= AppSettings.MaxPhotosOnScreen)
        {
            if (_rollingMode)
            {
                var oldest = _cards.Where(c => c.State == CardState.Resting).OrderBy(c => c.ZOrder).FirstOrDefault();
                if (oldest == null) return;
                oldest.State = CardState.FadingOut; oldest.FadeStartTime = DateTime.Now;
            }
            else { BeginClear(); return; }
        }

        var entry = _media[_nextIdx];
        int nextZ = _cards.Count > 0 ? _cards.Max(c => c.ZOrder) + 1 : 0;
        var fp    = ComputeFlightPath(entry.Width, entry.Height);

        PhotoCard card;
        if (entry.Kind == MediaKind.Video)
        {
            var vp = new VideoPlayer(entry.VideoPath!, entry.Width, entry.Height);
            card = new PhotoCard
            {
                Kind = MediaKind.Video, VideoPlayer = vp,
                PhotoWidth = entry.Width, PhotoHeight = entry.Height,
                X = fp.StartX, Y = fp.StartY, Rotation = fp.StartRot, Scale = 0.65f,
                StartX = fp.StartX, StartY = fp.StartY, StartRotation = fp.StartRot,
                TargetX = fp.TargetX, TargetY = fp.TargetY, TargetRotation = fp.TargetRot,
                LaunchTime = DateTime.Now, FlightDuration = fp.Duration, ZOrder = nextZ,
            };
        }
        else
        {
            card = new PhotoCard
            {
                Kind = entry.Kind, Frames = entry.Frames,
                PhotoWidth = entry.Width, PhotoHeight = entry.Height,
                X = fp.StartX, Y = fp.StartY, Rotation = fp.StartRot, Scale = 0.65f,
                StartX = fp.StartX, StartY = fp.StartY, StartRotation = fp.StartRot,
                TargetX = fp.TargetX, TargetY = fp.TargetY, TargetRotation = fp.TargetRot,
                LaunchTime = DateTime.Now, FlightDuration = fp.Duration, ZOrder = nextZ,
            };
        }

        _cards.Add(card);
        _nextIdx++;
    }

    private (float StartX, float StartY, float StartRot,
             float TargetX, float TargetY, float TargetRot, double Duration)
        ComputeFlightPath(int pw, int ph)
    {
        var (bSide, bTop, bBottom) = GetBorderDims();
        float cardW  = pw + bSide * 2;
        float cardH  = ph + bTop + bBottom;
        float half   = MathF.Sqrt(cardW * cardW + cardH * cardH) / 2f;
        float margin = half + 30f;
        float cx = ClientSize.Width, cy = ClientSize.Height;
        float extra = half + 60f;

        float targetX   = margin + _rng.NextSingle() * Math.Max(1f, cx - margin * 2f);
        float targetY   = margin + _rng.NextSingle() * Math.Max(1f, cy - margin * 2f);
        float targetRot = (_rng.NextSingle() - 0.5f) * 20f;
        float startRot  = (_rng.NextSingle() - 0.5f) * 120f;
        float startX, startY;
        switch (_rng.Next(4))
        {
            case 0:  startX = _rng.NextSingle() * cx; startY = -extra; break;
            case 1:  startX = cx + extra; startY = _rng.NextSingle() * cy; break;
            case 2:  startX = _rng.NextSingle() * cx; startY = cy + extra; break;
            default: startX = -extra; startY = _rng.NextSingle() * cy; break;
        }
        return (startX, startY, startRot, targetX, targetY, targetRot, 2.2 + _rng.NextDouble() * 1.6);
    }

    private void BeginClear()
    {
        if (_stage == Stage.Clearing) return;
        _stage = Stage.Clearing; _clearStart = DateTime.Now; _launchTimer.Stop();
    }

    private void Reset()
    {
        foreach (var c in _cards) c.Dispose();
        _cards.Clear();
        _nextIdx = 0;
        var shuffled = _media.OrderBy(_ => _rng.Next()).ToList();
        _media.Clear(); _media.AddRange(shuffled);
        _stage = Stage.Running;
        _launchTimer.Interval = Math.Max(500, AppSettings.LaunchIntervalSeconds * 1000);
        _launchTimer.Start();
        LaunchNext();
    }

    // ── Tick ──────────────────────────────────────────────────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        var now    = DateTime.Now;
        double dms = (now - _lastTickTime).TotalMilliseconds;
        _lastTickTime = now;

        if (_hasBackground && _bgKind == MediaKind.Animated && _bgFrames is { Count: > 1 })
        {
            _bgFrameElapsedMs += dms;
            int dur = _bgFrames[_bgFrameIdx].DurationMs;
            while (_bgFrameElapsedMs >= dur)
            {
                _bgFrameElapsedMs -= dur;
                _bgFrameIdx        = (_bgFrameIdx + 1) % _bgFrames.Count;
                dur                = _bgFrames[_bgFrameIdx].DurationMs;
            }
        }

        if (_stage == Stage.Clearing)
        {
            float p = (float)((now - _clearStart).TotalSeconds / ClearSecs);
            float a = Math.Max(0f, 1f - p);
            foreach (var c in _cards) c.Alpha = a;
            if (p >= 1f) Reset();
            _gl.Invalidate();
            return;
        }

        const double FadeOutSecs = 0.8;
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i].State == CardState.FadingOut)
            {
                float ft = (float)((now - _cards[i].FadeStartTime).TotalSeconds / FadeOutSecs);
                _cards[i].Alpha = Math.Max(0f, 1f - ft);
                if (ft >= 1f) { _cards[i].Dispose(); _cards.RemoveAt(i); }
            }
        }

        foreach (var card in _cards)
        {
            card.AdvanceFrame(dms);

            if (card.State == CardState.Flying)
            {
                float t    = Math.Clamp((float)((now - card.LaunchTime).TotalSeconds / card.FlightDuration), 0f, 1f);
                float posT = EaseOutCubic(t);
                float rotT = EaseOutSine(t);
                card.X        = Lerp(card.StartX,       card.TargetX,        posT);
                card.Y        = Lerp(card.StartY,        card.TargetY,        posT);
                card.Rotation = Lerp(card.StartRotation, card.TargetRotation, rotT);
                card.Scale    = Lerp(0.65f, 1f, posT);
                if (t >= 1f) card.State = CardState.Resting;
            }
        }

        _gl.Invalidate();
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(BgColor);

        if (_hasBackground)
        {
            SKBitmap? bgBmp = _bgKind == MediaKind.Video
                ? (_bgVideo?.HasFrame == true ? _bgVideo.CurrentFrame() : null)
                : _bgFrames?[_bgFrameIdx].Bitmap;

            if (bgBmp != null)
            {
                int sw = e.BackendRenderTarget.Width;
                int sh = e.BackendRenderTarget.Height;
                using var bp  = new SKPaint { FilterQuality = SKFilterQuality.Low };
                canvas.DrawBitmap(bgBmp, BgDestRect(bgBmp.Width, bgBmp.Height, sw, sh, _bgFit), bp);
                using var dim = new SKPaint { Color = new SKColor(0, 0, 0, 80) };
                canvas.DrawRect(0, 0, sw, sh, dim);
            }
        }

        if (_stage == Stage.Loading)
        { DrawMessage(canvas, e.BackendRenderTarget.Width, e.BackendRenderTarget.Height, "Loading…"); return; }

        if (_errorMessage != null)
        { DrawMessage(canvas, e.BackendRenderTarget.Width, e.BackendRenderTarget.Height, $"Error:\n{_errorMessage}"); return; }

        if (_media.Count == 0)
        { DrawMessage(canvas, e.BackendRenderTarget.Width, e.BackendRenderTarget.Height,
            "No media found in the configured folder.\n\nRun  PhotoSaverAnimated.scr /c  to choose a folder."); return; }

        foreach (var card in _cards.OrderBy(c => c.ZOrder))
            DrawCard(canvas, card);
    }

    private void DrawCard(SKCanvas canvas, PhotoCard card)
    {
        if (card.Alpha <= 0.01f) return;
        if (card.Kind == MediaKind.Video && !card.VideoPlayer!.HasFrame) return;

        var bmp  = card.CurrentBitmap;
        int pw   = bmp.Width;
        int ph   = bmp.Height;
        var (bSide, bTop, bBottom) = GetBorderDims();
        float cardW = pw + bSide * 2;
        float cardH = ph + bTop + bBottom;
        byte  alpha = (byte)(255 * card.Alpha);

        canvas.Save();
        canvas.Translate(card.X, card.Y);
        canvas.RotateDegrees(card.Rotation);
        canvas.Scale(card.Scale);

        using (var p = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(ShadowAlpha * card.Alpha)) })
            canvas.DrawRect(-cardW / 2f + ShadowOff, -cardH / 2f + ShadowOff, cardW, cardH, p);

        if (_borderStyle != CardBorderStyle.None)
        {
            var bc = _borderStyle == CardBorderStyle.Thin
                ? new SKColor(255, 255, 255, alpha) : new SKColor(252, 250, 244, alpha);
            using var p = new SKPaint { Color = bc };
            canvas.DrawRect(-cardW / 2f, -cardH / 2f, cardW, cardH, p);
        }

        float photoX = -pw / 2f;
        float photoY = -ph / 2f - (bBottom - bTop) / 2f;
        using var pp = new SKPaint { Color = SKColors.White.WithAlpha(alpha), FilterQuality = SKFilterQuality.Medium };
        canvas.DrawBitmap(bmp, new SKRect(photoX, photoY, photoX + pw, photoY + ph), pp);

        canvas.Restore();
    }

    private (float side, float top, float bottom) GetBorderDims() => _borderStyle switch
    {
        CardBorderStyle.Thin => (4f, 4f, 4f),
        CardBorderStyle.None => (0f, 0f, 0f),
        _                    => (PolaroidBorderSide, PolaroidBorderTop, PolaroidBorderBottom),
    };

    private static void DrawMessage(SKCanvas canvas, int w, int h, string text)
    {
        using var p = new SKPaint { Color = new SKColor(255,255,255,150), TextSize=26f, IsAntialias=true, TextAlign=SKTextAlign.Center };
        string[] lines = text.Split('\n');
        float lh = p.TextSize * 1.65f, cx = w / 2f, y = (h - lines.Length * lh) / 2f + p.TextSize;
        foreach (var l in lines) { canvas.DrawText(l.Trim(), cx, y, p); y += lh; }
    }

    private void HandleMouseMove(Point p)
    {
        if (!ReadyToExit) return;
        if (!_mouseTracked) { _lastMouse = p; _mouseTracked = true; return; }
        if (Math.Abs(p.X - _lastMouse.X) > 3 || Math.Abs(p.Y - _lastMouse.Y) > 3) RequestExit();
    }

    // Rotate all frames 90° CW and swap reported dimensions (portrait ↔ landscape).
    // Called at load time before the entry is added to _media, so bitmaps have no other owners.
    private static MediaEntry RotateEntry90(MediaEntry src)
    {
        var rotated = src.Frames.Select(f => {
            var bmp = ApplyExifOrientation(f.Bitmap, SKEncodedOrigin.LeftBottom);
            f.Bitmap.Dispose();
            return new AnimFrame { Bitmap = bmp, DurationMs = f.DurationMs };
        }).ToList();
        return new MediaEntry { Kind = src.Kind, Frames = rotated, Width = src.Height, Height = src.Width };
    }

    private static SKRect BgDestRect(int bw, int bh, int sw, int sh, BackgroundFit fit)
    {
        if (fit == BackgroundFit.Stretch) return new SKRect(0, 0, sw, sh);
        float scale = fit == BackgroundFit.Fit
            ? Math.Min((float)sw / bw, (float)sh / bh)
            : Math.Max((float)sw / bw, (float)sh / bh);   // Fill / cover
        float dw = bw * scale, dh = bh * scale;
        float ox = (sw - dw) / 2f,  oy = (sh - dh) / 2f;
        return new SKRect(ox, oy, ox + dw, oy + dh);
    }

    private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);
    private static float EaseOutSine(float t)  => MathF.Sin(t * MathF.PI / 2f);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
