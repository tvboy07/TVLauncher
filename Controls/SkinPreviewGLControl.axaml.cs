using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Silk.NET.OpenGL;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Numerics;
using TVLauncher.Services;

namespace TVLauncher.Controls;

/// <summary>
/// Visor 3D de skin de Minecraft: cuerpo completo, capa exterior, capa (cape) opcional,
/// animación de caminar + giro automático, rotación con mouse/touchpad.
/// </summary>
public partial class SkinPreviewGLControl : OpenGlControlBase
{
    private const float Deg = MathF.PI / 180f;

    private enum PartKind { Static, RightArm, LeftArm, RightLeg, LeftLeg, Cape, Hat }
    private readonly record struct ModelPart(int Start, int Count, Vector3 Pivot, PartKind Kind);
    private readonly List<ModelPart> _parts = new();

    private GL? _gl;
    private uint _program;
    private uint _vao;
    private uint _vbo;
    private uint _texture;
    private uint _capeTexture;
    private uint _hatTexture;

    private int _mvpLocation = -1;
    private int _skinTextureLocation = -1;

    private bool _isDragging;
    private Avalonia.Point _lastPointerPosition;
    private float _rotationX = -10f;
    private float _rotationY = 0f;

    private byte[]? _skinTextureData;
    private bool _textureLoaded;
    private bool _programValid;

    private byte[]? _capeData;
    private bool _capeDirty;
    private bool _capeLoaded;
    private bool _showCape = true;

    private byte[]? _hatData;
    private bool _hatDirty;
    private bool _hatLoaded;
    private bool _showHat;

    private bool _isSlim;
    private bool _geometryDirty;

    // Animación
    private bool _autoRotate = true;
    private bool _walking = true;
    private const float AutoRotateSpeed = 30f;   // grados por segundo
    private const float WalkSpeed = 5f;          // radianes por segundo (ciclo ~1.2 s)
    private const float WalkAmplitude = 35f;     // grados
    private float _walkPhase;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastFrameTime;

    private int _lastLoggedWidth = -1;
    private int _lastLoggedHeight = -1;

    public SkinPreviewGLControl()
    {
        Log.Info("[GL] SkinPreviewGLControl creado (modelo completo)");
    }

    // ---------- API pública ----------

    public void SetSkinTexture(byte[] textureData)
    {
        Log.Info($"[GL] SetSkinTexture ({textureData.Length} bytes)");
        _skinTextureData = textureData;
        _textureLoaded = false;
        RequestNextFrameRendering();
    }

    /// <summary>Textura de la capa (cape) del jugador. Pasa null si no tiene capa.</summary>
    public void SetCapeTexture(byte[]? capeData)
    {
        Log.Info($"[GL] SetCapeTexture ({(capeData == null ? "null" : capeData.Length + " bytes")})");
        _capeData = capeData;
        _capeDirty = true;
        RequestNextFrameRendering();
    }

    /// <summary>Muestra u oculta la capa (para el botón "quitar capa").</summary>
    public void SetCapeVisible(bool visible)
    {
        _showCape = visible;
        RequestNextFrameRendering();
    }

    public bool HasCape => _capeData != null;
    public bool IsCapeVisible => _showCape;

    public void SetHatTexture(byte[]? hatData)
    {
        Log.Info($"[GL] SetHatTexture ({(hatData == null ? "null" : hatData.Length + " bytes")})");
        _hatData = hatData;
        _hatDirty = true;
        RequestNextFrameRendering();
    }

    public void SetHatVisible(bool visible)
    {
        _showHat = visible;
        RequestNextFrameRendering();
    }

    public void SetAutoRotate(bool enabled) => _autoRotate = enabled;

    public void SetWalking(bool enabled)
    {
        _walking = enabled;
        if (!enabled) _walkPhase = 0f;
    }

    /// <summary>true = brazos de 3px (Alex / slim), false = classic (Steve)</summary>
    public void SetSlim(bool slim)
    {
        if (_isSlim == slim) return;
        _isSlim = slim;
        _geometryDirty = true;
        RequestNextFrameRendering();
    }

    // ---------- Ciclo de vida / input ----------

    private TopLevel? _topLevel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // OpenGlControlBase no es "hit-testable", así que escuchamos desde la ventana
        // (túnel, incluso si otro control marcó el evento como handled) y filtramos por bounds.
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel != null)
        {
            _topLevel.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnTopPressed,
                Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            _topLevel.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, OnTopMoved,
                Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            _topLevel.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, OnTopReleased,
                Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            _topLevel.AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, OnTopWheel,
                Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        }
        RequestNextFrameRendering();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel != null)
        {
            _topLevel.RemoveHandler(Avalonia.Input.InputElement.PointerPressedEvent, OnTopPressed);
            _topLevel.RemoveHandler(Avalonia.Input.InputElement.PointerMovedEvent, OnTopMoved);
            _topLevel.RemoveHandler(Avalonia.Input.InputElement.PointerReleasedEvent, OnTopReleased);
            _topLevel.RemoveHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, OnTopWheel);
            _topLevel = null;
        }
        _isDragging = false;
        base.OnDetachedFromVisualTree(e);
    }

    private bool IsInside(Avalonia.Input.PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        return pos.X >= 0 && pos.Y >= 0 && pos.X <= Bounds.Width && pos.Y <= Bounds.Height;
    }

    private void OnTopPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!IsInside(e)) return;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed || e.Pointer.Type != Avalonia.Input.PointerType.Mouse)
        {
            _isDragging = true;
            _lastPointerPosition = point.Position;
        }
    }

    private void OnTopMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!_isDragging) return;

        if (e.Pointer.Type == Avalonia.Input.PointerType.Mouse
            && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = false;
            return;
        }

        var currentPos = e.GetPosition(this);
        var delta = currentPos - _lastPointerPosition;
        _lastPointerPosition = currentPos;

        _rotationY += (float)delta.X * 0.8f;
        _rotationX = Math.Clamp(_rotationX + (float)delta.Y * 0.8f, -80f, 80f);
        RequestNextFrameRendering();
    }

    private void OnTopReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        _isDragging = false;
    }

    // Scroll con dos dedos en el touchpad (o rueda del mouse) también rota el modelo
    private void OnTopWheel(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (!IsInside(e)) return;
        _rotationY += (float)e.Delta.X * 12f;
        _rotationX = Math.Clamp(_rotationX - (float)e.Delta.Y * 12f, -80f, 80f);
        RequestNextFrameRendering();
    }

    // ---------- OpenGL ----------

    protected override unsafe void OnOpenGlInit(GlInterface glInterface)
    {
        try
        {
            _gl = GL.GetApi(glInterface.GetProcAddress);

            string version = _gl.GetStringS(StringName.Version) ?? "desconocida";
            string glslVersion = _gl.GetStringS(StringName.ShadingLanguageVersion) ?? "desconocida";
            bool isEs = version.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase)
                        || glslVersion.Contains("ES", StringComparison.OrdinalIgnoreCase);

            Log.Info($"[GL] Contexto: {version}");
            Log.Info($"[GL] GLSL: {glslVersion} (ES={isEs})");

            string header = isEs ? "#version 100\n" : "#version 120\n";
            string fsPrecision = isEs ? "precision mediump float;\n" : "";

            string vertexSource = header + """
                attribute vec3 aPos;
                attribute vec2 aTexCoord;
                varying vec2 vTexCoord;
                uniform mat4 mvp;
                void main()
                {
                    gl_Position = mvp * vec4(aPos, 1.0);
                    vTexCoord = aTexCoord;
                }
                """;

            // discard en vez de blending: los píxeles transparentes no escriben profundidad.
            string fragmentSource = header + fsPrecision + """
                varying vec2 vTexCoord;
                uniform sampler2D uSkin;
                void main()
                {
                    vec4 c = texture2D(uSkin, vTexCoord);
                    if (c.a < 0.5) discard;
                    gl_FragColor = vec4(c.rgb, 1.0);
                }
                """;

            uint vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
            uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

            _program = _gl.CreateProgram();
            _gl.AttachShader(_program, vertexShader);
            _gl.AttachShader(_program, fragmentShader);
            _gl.LinkProgram(_program);
            _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out int linkStatus);
            _programValid = linkStatus != 0;

            if (!_programValid)
                Log.Error($"[GL] Link falló: {_gl.GetProgramInfoLog(_program)}");
            else
                Log.Info("[GL] Programa enlazado correctamente");

            _gl.DetachShader(_program, vertexShader);
            _gl.DetachShader(_program, fragmentShader);
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);

            _mvpLocation = _gl.GetUniformLocation(_program, "mvp");
            _skinTextureLocation = _gl.GetUniformLocation(_program, "uSkin");

            BuildGeometry();

            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Lequal);
            _gl.Disable(EnableCap.CullFace);
            _gl.Disable(EnableCap.Blend);

            Log.Info($"[GL] Init OK (program={_program}, vao={_vao}, vbo={_vbo})");

            if (_skinTextureData != null)
                LoadTexture();
            if (_capeData != null)
                _capeDirty = true;

            RequestNextFrameRendering();
        }
        catch (Exception ex)
        {
            Log.Error($"[GL] Excepción en OnOpenGlInit: {ex}");
        }
    }

    private uint CompileShader(ShaderType type, string source)
    {
        if (_gl == null) return 0;
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
            Log.Error($"[GL] Compilación de {type} falló: {_gl.GetShaderInfoLog(shader)}");
        return shader;
    }

    private unsafe void BuildGeometry()
    {
        if (_gl == null) return;

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        UploadVertices();

        int posLocation = _gl.GetAttribLocation(_program, "aPos");
        int texLocation = _gl.GetAttribLocation(_program, "aTexCoord");
        Log.Info($"[GL] Atributos: aPos={posLocation}, aTexCoord={texLocation}");

        if (posLocation >= 0)
        {
            _gl.VertexAttribPointer((uint)posLocation, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray((uint)posLocation);
        }
        if (texLocation >= 0)
        {
            _gl.VertexAttribPointer((uint)texLocation, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray((uint)texLocation);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    // Asume que _vbo ya está enlazado a ArrayBuffer
    private unsafe void UploadVertices()
    {
        if (_gl == null) return;
        float[] vertices = BuildFullModelVertices();
        fixed (float* ptr = vertices)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
        }
    }

    // ---------- Geometría ----------

    private float[] BuildFullModelVertices()
    {
        var v = new List<float>(13 * 24 * 5);
        _parts.Clear();
        float aw = _isSlim ? 3f : 4f; // ancho de brazo

        void EndPart(int start, Vector3 pivot, PartKind kind)
            => _parts.Add(new ModelPart(start, v.Count / 5 - start, pivot, kind));

        int s;

        // Cabeza
        s = v.Count / 5;
        AddBox(v, -4, 12, -4, 8, 8, 8, 0, 0);
        AddBox(v, -4, 12, -4, 8, 8, 8, 32, 0, 0.5f, skipBottom: true);
        EndPart(s, Vector3.Zero, PartKind.Static);

        // Torso
        s = v.Count / 5;
        AddBox(v, -4, 0, -2, 8, 12, 4, 16, 16);
        AddBox(v, -4, 0, -2, 8, 12, 4, 16, 32, 0.25f);
        EndPart(s, Vector3.Zero, PartKind.Static);

        // Brazo derecho del personaje (lado -X)
        s = v.Count / 5;
        AddBox(v, -4 - aw, 0, -2, aw, 12, 4, 40, 16);
        AddBox(v, -4 - aw, 0, -2, aw, 12, 4, 40, 32, 0.25f);
        EndPart(s, new Vector3(-4 - aw / 2f, 10, 0), PartKind.RightArm);

        // Brazo izquierdo (lado +X)
        s = v.Count / 5;
        AddBox(v, 4, 0, -2, aw, 12, 4, 32, 48);
        AddBox(v, 4, 0, -2, aw, 12, 4, 48, 48, 0.25f);
        EndPart(s, new Vector3(4 + aw / 2f, 10, 0), PartKind.LeftArm);

        // Pierna derecha
        s = v.Count / 5;
        AddBox(v, -4, -12, -2, 4, 12, 4, 0, 16);
        AddBox(v, -4, -12, -2, 4, 12, 4, 0, 32, 0.25f);
        EndPart(s, new Vector3(-2, 0, 0), PartKind.RightLeg);

        // Pierna izquierda
        s = v.Count / 5;
        AddBox(v, 0, -12, -2, 4, 12, 4, 16, 48);
        AddBox(v, 0, -12, -2, 4, 12, 4, 0, 48, 0.25f);
        EndPart(s, new Vector3(2, 0, 0), PartKind.LeftLeg);

        // Capa (cape): caja de 10x16x1 colgando del hombro. Coordenadas relativas al pivote.
        // Se gira 180° en Y al dibujar para que la cara exterior mire hacia la espalda.
        s = v.Count / 5;
        AddBox(v, -5, -16, 0, 10, 16, 1, 0, 0);
        EndPart(s, new Vector3(0, 12, -2.3f), PartKind.Cape);

        // Sombrero TVLauncher: ligeramente más grande que la cabeza, inclinado hacia delante
        float hw = 5.2f;
        float hd = 5.2f;
        float hh = 2.6f;
        float br = 6.2f;
        float bd = 6.2f;
        float bh = 0.9f;
        s = v.Count / 5;
        // Ala del sombrero
        AddBox(v, -br / 2f, 8.6f, -bd / 2f + 0.8f, br, bh, bd, 0, 0);
        // Copa
        AddBox(v, -hw / 2f, 9.5f, -hd / 2f + 0.6f, hw, hh, hd, 0, 0);
        EndPart(s, new Vector3(0, 16, 0), PartKind.Hat);

        return v.ToArray();
    }

    private static void Vtx(List<float> l, float x, float y, float z, float u, float v)
    {
        // Las coordenadas de textura vienen en píxeles de la grilla 64x64.
        // Dividir entre 64 funciona también para texturas HD (128x128, etc.).
        l.Add(x); l.Add(y); l.Add(z); l.Add(u / 64f); l.Add(v / 64f);
    }

    /// <summary>
    /// Caja con el layout estándar de skins de Minecraft.
    /// (tx,ty) = esquina superior izquierda de la región de la caja en la textura.
    /// w,h,d = ancho, alto, profundidad. El frente mira a +Z (hacia la cámara).
    /// </summary>
    private void AddBox(List<float> l, float x, float y, float z, float w, float h, float d, int tx, int ty, float inflate = 0f, bool skipBottom = false)
    {
        float x1 = x - inflate, y1 = y - inflate, z1 = z - inflate;
        float x2 = x + w + inflate, y2 = y + h + inflate, z2 = z + d + inflate;

        float u0 = tx, u1 = tx + d, u2 = tx + d + w, u3 = tx + d + w + d, u4 = tx + d + w + d + w;
        float v0 = ty, v1 = ty + d, v2 = ty + d + h;

        // Frente (+Z)
        Vtx(l, x1, y1, z2, u1, v2); Vtx(l, x2, y1, z2, u2, v2); Vtx(l, x2, y2, z2, u2, v1); Vtx(l, x1, y2, z2, u1, v1);
        // Espalda (-Z)
        Vtx(l, x2, y1, z1, u3, v2); Vtx(l, x1, y1, z1, u4, v2); Vtx(l, x1, y2, z1, u4, v1); Vtx(l, x2, y2, z1, u3, v1);
        // Lado derecho del personaje (-X)
        Vtx(l, x1, y1, z1, u0, v2); Vtx(l, x1, y1, z2, u1, v2); Vtx(l, x1, y2, z2, u1, v1); Vtx(l, x1, y2, z1, u0, v1);
        // Lado izquierdo del personaje (+X)
        Vtx(l, x2, y1, z2, u2, v2); Vtx(l, x2, y1, z1, u3, v2); Vtx(l, x2, y2, z1, u3, v1); Vtx(l, x2, y2, z2, u2, v1);
        // Arriba (+Y)
        Vtx(l, x1, y2, z2, u1, v1); Vtx(l, x2, y2, z2, u2, v1); Vtx(l, x2, y2, z1, u2, v0); Vtx(l, x1, y2, z1, u1, v0);
        // Abajo (-Y)
        if (!skipBottom)
        {
            Vtx(l, x1, y1, z2, u2, v0); Vtx(l, x2, y1, z2, u3, v0); Vtx(l, x2, y1, z1, u3, v1); Vtx(l, x1, y1, z1, u2, v1);
        }
    }

    // ---------- Texturas ----------

    // Skins viejas 64x32 -> 64x64
    private static SKBitmap ToModernLayout(SKBitmap src)
    {
        int s = Math.Max(1, src.Width / 64);
        bool legacy = src.Height * 2 == src.Width;

        var info = new SKImageInfo(src.Width, src.Width, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var dst = new SKBitmap(info);
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(src, 0, 0);

        if (legacy)
        {
            MirrorLimb(canvas, src, 0, 16, 16, 48, s);
            MirrorLimb(canvas, src, 40, 16, 32, 48, s);
        }
        return dst;
    }

    // Capas: 64x32 (o 22x17 viejo, y versiones HD) -> lienzo cuadrado, la capa queda arriba a la izquierda
    private static SKBitmap ToCapeCanvas(SKBitmap src)
    {
        int s = src.Width % 64 == 0 ? src.Width / 64 : Math.Max(1, src.Width / 22);
        int size = 64 * s;
        var dst = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }

    // Minecraft fuerza alfa=255 en la capa base (cabeza, cuerpo/brazos/piernas). Hacemos lo mismo.
    private static unsafe void MakeBaseLayerOpaque(SKBitmap bmp)
    {
        int s = Math.Max(1, bmp.Width / 64);
        int stride = bmp.RowBytes;
        var px = new Span<byte>((void*)bmp.GetPixels(), stride * bmp.Height);
        FillAlpha(px, stride, s, 0, 0, 32, 16);
        FillAlpha(px, stride, s, 0, 16, 64, 32);
        FillAlpha(px, stride, s, 16, 48, 48, 64);
    }

    private static void FillAlpha(Span<byte> px, int stride, int s, int x0, int y0, int x1, int y1)
    {
        for (int y = y0 * s; y < y1 * s; y++)
            for (int x = x0 * s; x < x1 * s; x++)
                px[y * stride + x * 4 + 3] = 255;
    }

    private static void MirrorLimb(SKCanvas c, SKBitmap src, int sx, int sy, int dx, int dy, int s)
    {
        FlipRegion(c, src, sx + 4, sy, 4, 4, dx + 4, dy, s);          // arriba
        FlipRegion(c, src, sx + 8, sy, 4, 4, dx + 8, dy, s);          // abajo
        FlipRegion(c, src, sx, sy + 4, 4, 12, dx + 8, dy + 4, s);     // derecha -> izquierda
        FlipRegion(c, src, sx + 4, sy + 4, 4, 12, dx + 4, dy + 4, s); // frente
        FlipRegion(c, src, sx + 8, sy + 4, 4, 12, dx, dy + 4, s);     // izquierda -> derecha
        FlipRegion(c, src, sx + 12, sy + 4, 4, 12, dx + 12, dy + 4, s); // espalda
    }

    private static void FlipRegion(SKCanvas c, SKBitmap src, int sx, int sy, int w, int h, int dx, int dy, int s)
    {
        c.Save();
        c.Translate((dx + w) * s, dy * s);
        c.Scale(-1, 1);
        c.DrawBitmap(src,
            new SKRect(sx * s, sy * s, (sx + w) * s, (sy + h) * s),
            new SKRect(0, 0, w * s, h * s));
        c.Restore();
    }

    private unsafe uint CreateTexture(SKBitmap bmp)
    {
        var gl = _gl!;
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)bmp.Width, (uint)bmp.Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, (void*)bmp.GetPixels());
        gl.BindTexture(TextureTarget.Texture2D, 0);
        return tex;
    }

    private void LoadTexture()
    {
        if (_gl == null || _skinTextureData == null) return;
        try
        {
            if (_texture != 0) { _gl.DeleteTexture(_texture); _texture = 0; }

            using var decoded = SKBitmap.Decode(_skinTextureData);
            if (decoded == null) { Log.Error("[GL] No se pudo decodificar el PNG del skin"); return; }

            Log.Info($"[GL] Skin dimensions: {decoded.Width}x{decoded.Height}");

            using var skin = ToModernLayout(decoded);
            MakeBaseLayerOpaque(skin);

            _texture = CreateTexture(skin);
            _textureLoaded = true;
            Log.Info($"[GL] Textura cargada {skin.Width}x{skin.Height} (id={_texture})");
        }
        catch (Exception ex) { Log.Error($"[GL] Error cargando textura: {ex}"); }
    }

    private void LoadCapeTexture()
    {
        if (_gl == null) return;
        try
        {
            if (_capeTexture != 0) { _gl.DeleteTexture(_capeTexture); _capeTexture = 0; }
            _capeLoaded = false;

            if (_capeData == null) return;

            using var decoded = SKBitmap.Decode(_capeData);
            if (decoded == null) { Log.Error("[GL] No se pudo decodificar el PNG de la capa"); return; }

            using var canvas = ToCapeCanvas(decoded);
            _capeTexture = CreateTexture(canvas);
            _capeLoaded = true;
            Log.Info($"[GL] Capa cargada {decoded.Width}x{decoded.Height} (id={_capeTexture})");
        }
        catch (Exception ex) { Log.Error($"[GL] Error cargando capa: {ex}"); }
    }

    private void LoadHatTexture()
    {
        if (_gl == null) return;
        try
        {
            if (_hatTexture != 0) { _gl.DeleteTexture(_hatTexture); _hatTexture = 0; }
            _hatLoaded = false;

            if (_hatData == null) return;

            using var decoded = SKBitmap.Decode(_hatData);
            if (decoded == null) { Log.Error("[GL] No se pudo decodificar el PNG del sombrero"); return; }

            _hatTexture = CreateTexture(decoded);
            _hatLoaded = true;
            Log.Info($"[GL] Sombrero cargado {decoded.Width}x{decoded.Height} (id={_hatTexture})");
        }
        catch (Exception ex) { Log.Error($"[GL] Error cargando sombrero: {ex}"); }
    }

    // ---------- Render ----------

    private static Matrix4x4 About(Vector3 pivot, float angle)
        => Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationX(angle) * Matrix4x4.CreateTranslation(pivot);

    private Matrix4x4 PartMatrix(in ModelPart p, float walk)
    {
        switch (p.Kind)
        {
            case PartKind.RightArm: return About(p.Pivot, -walk);
            case PartKind.LeftArm:  return About(p.Pivot, walk);
            case PartKind.RightLeg: return About(p.Pivot, walk);
            case PartKind.LeftLeg:  return About(p.Pivot, -walk);
            case PartKind.Cape:
            {
                float tilt = (6f + (_walking ? 5f + 4f * MathF.Sin(_walkPhase * 2f) : 0f)) * Deg;
                return Matrix4x4.CreateRotationY(MathF.PI)
                     * Matrix4x4.CreateRotationX(tilt)
                     * Matrix4x4.CreateTranslation(p.Pivot);
            }
            case PartKind.Hat:
            {
                float tilt = -12f * Deg;
                float bob = _walking ? MathF.Sin(_walkPhase) * 1.2f : 0f;
                return Matrix4x4.CreateRotationX(tilt)
                     * Matrix4x4.CreateTranslation(p.Pivot + new Vector3(0, bob, 0.6f));
            }
            default: return Matrix4x4.Identity;
        }
    }

    protected override unsafe void OnOpenGlRender(GlInterface glInterface, int fb)
    {
        if (_gl == null)
        {
            _gl = GL.GetApi(glInterface.GetProcAddress);
            if (_gl == null) return;
        }

        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int width = Math.Max(1, (int)(Bounds.Width * scaling));
        int height = Math.Max(1, (int)(Bounds.Height * scaling));

        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(0.08f, 0.08f, 0.08f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (width != _lastLoggedWidth || height != _lastLoggedHeight)
        {
            Log.Info($"[GL] Render viewport {width}x{height} (scaling {scaling})");
            _lastLoggedWidth = width;
            _lastLoggedHeight = height;
        }

        // Animación con delta time
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(now - _lastFrameTime, 0.1);
        _lastFrameTime = now;
        if (_autoRotate && !_isDragging)
        {
            _rotationY += AutoRotateSpeed * dt;
            if (_rotationY >= 360f) _rotationY -= 360f;
        }
        if (_walking)
        {
            _walkPhase += WalkSpeed * dt;
            if (_walkPhase > MathF.PI * 200f) _walkPhase -= MathF.PI * 200f;
        }

        if (!_textureLoaded && _skinTextureData != null)
            LoadTexture();

        if (_capeDirty)
        {
            LoadCapeTexture();
            _capeDirty = false;
        }

        if (_hatDirty)
        {
            LoadHatTexture();
            _hatDirty = false;
        }

        if (_geometryDirty && _vbo != 0)
        {
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            UploadVertices();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            _geometryDirty = false;
        }

        bool ready = _programValid && _vao != 0 && _textureLoaded && _texture != 0 && _mvpLocation >= 0 && _skinTextureLocation >= 0;
        if (ready)
        {
            _gl.UseProgram(_program);

            // El modelo va de y=-12 a y=20 -> centro en y=4. Se rota alrededor de su centro.
            Matrix4x4 model =
                Matrix4x4.CreateTranslation(0, -4, 0) *
                Matrix4x4.CreateRotationY(_rotationY * Deg) *
                Matrix4x4.CreateRotationX(_rotationX * Deg);
            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 58), Vector3.Zero, Vector3.UnitY);
            float aspect = width / (float)height;
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, 1f, 200f);
            Matrix4x4 viewProj = model * view * projection;

            float walk = _walking ? MathF.Sin(_walkPhase) * WalkAmplitude * Deg : 0f;
            bool capeReady = _showCape && _capeLoaded && _capeTexture != 0;
            bool hatReady = _showHat && _hatLoaded && _hatTexture != 0;

            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.Uniform1(_skinTextureLocation, 0);
            _gl.BindVertexArray(_vao);

            uint boundTex = 0;
            foreach (var part in _parts)
            {
                if (part.Kind == PartKind.Cape && !capeReady) continue;
                if (part.Kind == PartKind.Hat && !hatReady) continue;

                uint tex = part.Kind == PartKind.Cape ? _capeTexture : part.Kind == PartKind.Hat ? _hatTexture : _texture;
                if (tex != boundTex)
                {
                    _gl.BindTexture(TextureTarget.Texture2D, tex);
                    boundTex = tex;
                }

                Matrix4x4 mvp = PartMatrix(part, walk) * viewProj;
                _gl.UniformMatrix4(_mvpLocation, 1, false, (float*)&mvp);

                int end = part.Start + part.Count;
                for (int i = part.Start; i < end; i += 4)
                    _gl.DrawArrays(PrimitiveType.TriangleFan, i, 4);
            }

            _gl.BindVertexArray(0);
            _gl.BindTexture(TextureTarget.Texture2D, 0);
        }

        RequestNextFrameRendering();
    }

    protected override void OnOpenGlDeinit(GlInterface glInterface)
    {
        Log.Info("[GL] OnOpenGlDeinit");
        if (_gl != null)
        {
            if (_vao != 0) _gl.DeleteVertexArray(_vao);
            if (_vbo != 0) _gl.DeleteBuffer(_vbo);
            if (_program != 0) _gl.DeleteProgram(_program);
            if (_texture != 0) _gl.DeleteTexture(_texture);
            if (_capeTexture != 0) _gl.DeleteTexture(_capeTexture);
            if (_hatTexture != 0) _gl.DeleteTexture(_hatTexture);
        }
        _vao = 0; _vbo = 0; _program = 0; _texture = 0; _capeTexture = 0; _hatTexture = 0;
        _textureLoaded = false; _programValid = false; _capeLoaded = false; _hatLoaded = false;
        _capeDirty = _capeData != null;
        _hatDirty = _hatData != null;
    }
}