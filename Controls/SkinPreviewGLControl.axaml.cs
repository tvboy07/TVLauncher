using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Silk.NET.OpenGL;
using SkiaSharp;
using System;
using System.Numerics;
using TVLauncher.Services;

namespace TVLauncher.Controls;

/// <summary>
/// Visor 3D completo del skin de Minecraft (cuerpo entero + capa exterior/hat).
/// Soporta skins 64x64 (con segunda capa) y 64x32 (sin capa exterior).
/// </summary>
public partial class SkinPreviewGLControl : OpenGlControlBase
{
    private GL? _gl;
    private uint _program;
    private uint _vao;
    private uint _vbo;
    private uint _texture;

    private int _mvpLocation = -1;
    private int _skinTextureLocation = -1;

    private float _rotationY = 0f;
    private const float _rotationX = -10f;

    private byte[]? _skinTextureData;
    private bool _textureLoaded;
    private bool _programValid;

    private int _textureWidth;
    private int _textureHeight;
    private bool _hasOuterLayer;

    private int _lastLoggedWidth = -1;
    private int _lastLoggedHeight = -1;

    public SkinPreviewGLControl()
    {
        Log.Info("[GL] SkinPreviewGLControl creado (modelo completo)");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Log.Info($"[GL] OnAttachedToVisualTree (bounds {Bounds.Width}x{Bounds.Height})");
        RequestNextFrameRendering();
    }

    public void SetSkinTexture(byte[] textureData)
    {
        Log.Info($"[GL] SetSkinTexture ({textureData.Length} bytes)");
        _skinTextureData = textureData;
        _textureLoaded = false;
        RequestNextFrameRendering();
    }

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

            string vertexSource, fragmentSource;
            if (isEs)
            {
                vertexSource = """
                    #version 100
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

                fragmentSource = """
                    #version 100
                    precision mediump float;
                    varying vec2 vTexCoord;
                    uniform sampler2D uSkin;
                    void main()
                    {
                        gl_FragColor = texture2D(uSkin, vTexCoord);
                    }
                    """;
            }
            else
            {
                vertexSource = """
                    #version 120
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

                fragmentSource = """
                    #version 120
                    varying vec2 vTexCoord;
                    uniform sampler2D uSkin;
                    void main()
                    {
                        gl_FragColor = texture2D(uSkin, vTexCoord);
                    }
                    """;
            }

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
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            Log.Info($"[GL] Init OK (program={_program}, vao={_vao}, vbo={_vbo})");

            if (_skinTextureData != null)
                LoadTexture();

            RequestNextFrameRendering();
        }
        catch (Exception ex)
        {
            Log.Error($"[GL] Excepción en OnOpenGlInit: {ex}");
        }
    }

    private unsafe uint CompileShader(ShaderType type, string source)
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

        // Geometría completa: cabeza, sombrero/overlay, cuerpo, brazos, piernas (base + overlay)
        // UVs estándar Minecraft 1.8+ (64x64). Funciona para 64x32 donde las áreas de overlay están fuera pero no se usan.
        float[] vertices = BuildFullModelVertices();
        
        fixed (float* ptr = vertices)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
        }

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

    private float[] BuildFullModelVertices()
    {
        // Cada cara: 4 vértices * (x,y,z,u,v) = 20 floats. Cada cubo = 6 caras * 4 = 24 vértices = 120 floats
        // Partes: Cabeza(1)+Hat(1)+Cuerpo(1)+Jacket(1)+BrazoDer(1)+BrazoIzq(1)+SleeveDer(1)+SleeveIzq(1)+PiernaDer(1)+PantalonDer(1)+PiernaIzq(1)+PantalonIzq(1) = 12 cubos
        System.Collections.Generic.List<float> verts = new(12 * 120);

        float s = 1f;
        // Posiciones aproximadas para modelo tipo Steve
        // Cabeza
        AddCube(verts, -4*s, 8*s, -4*s, 8*s, s); // head base
        AddCubeScaled(verts, -4.2f*s, 7.8f*s, -4.2f*s, 8.4f*s, 1.05f, 32, 8); // hat overlay (64x64 hat UVs at x=32)
        
        // Cuerpo
        AddCube(verts, -4*s, 0*s, -2*s, 8*s, 4*s, 2*s, 16, 16); // body
        AddCubeScaled(verts, -4.05f*s, -0.05f*s, -2.05f*s, 8.1f*s, 4.1f*s, 2.1f*s, 1.05f, 16, 32); // jacket
        
        // Brazo derecho
        AddCube(verts, -8*s, 0*s, -2*s, 4*s, 4*s, 2*s, 40, 16);
        AddCubeScaled(verts, -8.05f*s, -0.05f*s, -2.05f*s, 4.1f*s, 4.1f*s, 2.1f*s, 1.05f, 40, 32);
        
        // Brazo izquierdo  
        AddCubeMirrored(verts, 4*s, 0*s, -2*s, 4*s, 4*s, 2*s, 32, 48);
        AddCubeScaledMirrored(verts, 4*s-0.05f, -0.05f*s, -2.05f*s, 4.1f*s, 4.1f*s, 2.1f*s, 1.05f, 48, 48);
        
        // Pierna derecha
        AddCube(verts, -4*s, -12*s, -2*s, 4*s, 4*s, 2*s, 0, 16);
        AddCubeScaled(verts, -4.05f*s, -12.05f*s, -2.05f*s, 4.1f*s, 4.1f*s, 2.1f*s, 1.05f, 0, 32);
        
        // Pierna izquierda
        AddCube(verts, 0*s, -12*s, -2*s, 4*s, 4*s, 2*s, 16, 48);
        AddCubeScaled(verts, -0.05f*s, -12.05f*s, -2.05f*s, 4.1f*s, 4.1f*s, 2.1f*s, 1.05f, 0, 48);

        return verts.ToArray();
    }

    private void AddCube(System.Collections.Generic.List<float> verts, float x, float y, float z, float w, float h, float d, int uvX = 0, int uvY = 0)
    {
        float x1 = x, y1 = y, z1 = z;
        float x2 = x + w, y2 = y + h, z2 = z + d;
        float u = uvX / 64f, v = uvY / 64f, us = 8f/64f, vs = 8f/64f;
        
        // Simplified cube with proper UVs for Minecraft layout
        // Front
        verts.AddRange(new float[] { x1,y1,z1,u,v+vs, x2,y1,z1,u+us,v+vs, x2,y2,z1,u+us,v, x1,y2,z1,u,v });
        // Back  
        verts.AddRange(new float[] { x2,y1,z2,u+2*us,v+vs, x1,y1,z2,u+us,v+vs, x1,y2,z2,u+us,v, x2,y2,z2,u+2*us,v });
        // Left
        verts.AddRange(new float[] { x1,y1,z2,u,v+vs, x1,y1,z1,u+us,v+vs, x1,y2,z1,u+us,v, x1,y2,z2,u,v });
        // Right
        verts.AddRange(new float[] { x2,y1,z1,u+3*us,v+vs, x2,y1,z2,u+2*us,v+vs, x2,y2,z2,u+2*us,v, x2,y2,z1,u+3*us,v });
        // Top
        verts.AddRange(new float[] { x1,y2,z1,u+us,v, x2,y2,z1,u+2*us,v, x2,y2,z2,u+2*us,v-vs, x1,y2,z2,u+us,v-vs });
        // Bottom
        verts.AddRange(new float[] { x1,y1,z2,u+us,v+vs, x2,y1,z2,u+2*us,v+vs, x2,y1,z1,u+2*us,v, x1,y1,z1,u+us,v });
    }

    private void AddCubeScaled(System.Collections.Generic.List<float> verts, float x, float y, float z, float w, float h, float d, float scale, int uvX = 32, int uvY = 8)
    {
        float cx = x + w/2f, cy = y + h/2f, cz = z + d/2f;
        float nw = w*scale, nh = h*scale, nd = d*scale;
        AddCube(verts, cx-nw/2f, cy-nh/2f, cz-nd/2f, nw, nh, nd, uvX, uvY);
    }

    private void AddCubeMirrored(System.Collections.Generic.List<float> verts, float x, float y, float z, float w, float h, float d, int uvX = 32, int uvY = 48) => AddCube(verts, x, y, z, w, h, d, uvX, uvY);
    private void AddCubeScaledMirrored(System.Collections.Generic.List<float> verts, float x, float y, float z, float w, float h, float d, float scale, int uvX = 48, int uvY = 48) => AddCubeScaled(verts, x, y, z, w, h, d, scale, uvX, uvY);

    private void AddCube(System.Collections.Generic.List<float> verts, float x1, float y1, float z1, float w, float h, float d)
    {
        float x2 = x1 + w, y2 = y1 + h, z2 = z1 + d;
        float u = 0f, v = 8f/64f, us = 8f/64f, vs = 8f/64f;
        verts.AddRange(new float[] { x1,y1,z1,u,v+vs, x2,y1,z1,u+us,v+vs, x2,y2,z1,u+us,v, x1,y2,z1,u,v });
        verts.AddRange(new float[] { x2,y1,z2,u+2*us,v+vs, x1,y1,z2,u+us,v+vs, x1,y2,z2,u+us,v, x2,y2,z2,u+2*us,v });
        verts.AddRange(new float[] { x1,y1,z2,u,v+vs, x1,y1,z1,u+us,v+vs, x1,y2,z1,u+us,v, x1,y2,z2,u,v });
        verts.AddRange(new float[] { x2,y1,z1,u+3*us,v+vs, x2,y1,z2,u+2*us,v+vs, x2,y2,z2,u+2*us,v, x2,y2,z1,u+3*us,v });
        verts.AddRange(new float[] { x1,y2,z1,u+us,v, x2,y2,z1,u+2*us,v, x2,y2,z2,u+2*us,v-vs, x1,y2,z2,u+us,v-vs });
        verts.AddRange(new float[] { x1,y1,z2,u+us,v+vs, x2,y1,z2,u+2*us,v+vs, x2,y1,z1,u+2*us,v, x1,y1,z1,u+us,v });
    }

    private unsafe void LoadTexture()
    {
        if (_gl == null || _skinTextureData == null) return;
        try
        {
            if (_texture != 0) { _gl.DeleteTexture(_texture); _texture = 0; }

            using var bitmap = SKBitmap.Decode(_skinTextureData);
            if (bitmap == null) { Log.Error("[GL] No se pudo decodificar el PNG del skin"); return; }

            _textureWidth = bitmap.Width;
            _textureHeight = bitmap.Height;
            _hasOuterLayer = _textureWidth == 64 && _textureHeight == 64;
            Log.Info($"[GL] Skin dimensions: {_textureWidth}x{_textureHeight}, capa exterior: {_hasOuterLayer}");

            using var rgba = bitmap.Copy(SKColorType.Rgba8888);
            if (rgba == null) { Log.Error("[GL] No se pudo convertir el skin a RGBA8888"); return; }

            _texture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _texture);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)rgba.Width, (uint)rgba.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)rgba.GetPixels());
            _gl.BindTexture(TextureTarget.Texture2D, 0);

            _textureLoaded = true;
            Log.Info($"[GL] Textura cargada {_textureWidth}x{_textureHeight} (id={_texture}) con blending alfa habilitado");
        }
        catch (Exception ex) { Log.Error($"[GL] Error cargando textura: {ex}"); }
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

        if (!_textureLoaded && _skinTextureData != null)
            LoadTexture();

        bool ready = _programValid && _vao != 0 && _textureLoaded && _texture != 0 && _mvpLocation >= 0 && _skinTextureLocation >= 0;
        if (ready)
        {
            _gl.UseProgram(_program);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            Matrix4x4 model = Matrix4x4.CreateRotationX(_rotationX * MathF.PI / 180f) * Matrix4x4.CreateRotationY(_rotationY * MathF.PI / 180f);
            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 2, 45), new Vector3(0, -2, 0), Vector3.UnitY);
            float aspect = width / (float)height;
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, 0.1f, 150f);
            Matrix4x4 mvp = model * view * projection;

            _gl.UniformMatrix4(_mvpLocation, 1, false, (float*)&mvp);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _texture);
            _gl.Uniform1(_skinTextureLocation, 0);

            _gl.BindVertexArray(_vao);
            // Dibujar modelo completo: cada cara como TriangleFan (4 vértices)
            int totalVerts = 12 * 24;
            for (int i = 0; i < totalVerts; i += 4)
                _gl.DrawArrays(PrimitiveType.TriangleFan, i, 4);
            _gl.BindVertexArray(0);
            _gl.BindTexture(TextureTarget.Texture2D, 0);

            _rotationY += 0.4f;
            if (_rotationY >= 360f) _rotationY -= 360f;
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
        }
        _vao = 0; _vbo = 0; _program = 0; _texture = 0;
        _textureLoaded = false; _programValid = false;
    }
}
