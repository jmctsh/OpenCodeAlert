using System;
using System.IO;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Headless
{
	public class HeadlessPlatform : IPlatform
	{
		public IPlatformWindow CreateWindow(Size size, WindowMode windowMode, float scaleModifier, int vertexBatchSize, int indexBatchSize, int videoDisplay, GLProfile profile)
		{
			return new HeadlessWindow(size, windowMode, scaleModifier, vertexBatchSize, indexBatchSize, videoDisplay, profile);
		}

		public ISoundEngine CreateSound(string device)
		{
			return new DummySoundEngine();
		}

		public IFont CreateFont(byte[] data)
		{
			return new HeadlessFont();
		}
	}

	public class HeadlessWindow : IPlatformWindow
	{
		public IGraphicsContext Context { get; } = new HeadlessGraphicsContext();
		public Size NativeWindowSize { get; }
		public Size EffectiveWindowSize { get; }
		public float NativeWindowScale => 1.0f;
		public float EffectiveWindowScale => 1.0f;
		public Size SurfaceSize { get; }
		public int DisplayCount => 1;
		public int CurrentDisplay => 0;
		public bool HasInputFocus => false;
		public bool IsSuspended => false;
		public GLProfile GLProfile => GLProfile.Automatic;
		public GLProfile[] SupportedGLProfiles => new[] { GLProfile.Automatic };

		public event Action<float, float, float, float> OnWindowScaleChanged { add { } remove { } }

		public HeadlessWindow(Size size, WindowMode windowMode, float scaleModifier, int vertexBatchSize, int indexBatchSize, int videoDisplay, GLProfile profile)
		{
			NativeWindowSize = size;
			EffectiveWindowSize = size;
			SurfaceSize = size;
		}

		public void PumpInput(IInputHandler inputHandler) { }
		public string GetClipboardText() => "";
		public bool SetClipboardText(string text) => false;
		public void GrabWindowMouseFocus() { }
		public void ReleaseWindowMouseFocus() { }
		public IHardwareCursor CreateHardwareCursor(string name, Size size, byte[] data, int2 hotspot, bool pixelDouble) => new HeadlessCursor();
		public void SetHardwareCursor(IHardwareCursor cursor) { }
		public void SetWindowTitle(string title) { }
		public void SetRelativeMouseMode(bool mode) { }
		public void SetScaleModifier(float scale) { }
		public void Dispose() { }
	}

	public class HeadlessCursor : IHardwareCursor
	{
		public void Dispose() { }
	}

	public class HeadlessGraphicsContext : IGraphicsContext
	{
		public string GLVersion => "Headless 1.0";
		public void Clear() { }
		public void ClearDepthBuffer() { }
		public IFrameBuffer CreateFrameBuffer(Size s) => new HeadlessFrameBuffer(s);
		public IFrameBuffer CreateFrameBuffer(Size s, Color clearColor) => new HeadlessFrameBuffer(s);
		public IIndexBuffer CreateIndexBuffer(uint[] indices) => new HeadlessIndexBuffer();
		public IShader CreateShader(IShaderBindings shaderBindings) => new HeadlessShader();
		public ITexture CreateTexture() => new HeadlessTexture();
		public IVertexBuffer<T> CreateVertexBuffer<T>(int size) where T : struct => new HeadlessVertexBuffer<T>();
		public T[] CreateVertices<T>(int size) where T : struct => new T[size];
		public void DisableDepthBuffer() { }
		public void DisableScissor() { }
		public void DrawElements(int numIndices, int offset) { }
		public void DrawPrimitives(PrimitiveType pt, int firstVertex, int numVertices) { }
		public void EnableDepthBuffer() { }
		public void EnableScissor(int x, int y, int width, int height) { }
		public void Present() { }
		public void SetBlendMode(BlendMode mode) { }
		public void SetVSyncEnabled(bool enabled) { }
		public void Dispose() { }
	}

	public class HeadlessFrameBuffer : IFrameBuffer
	{
		public ITexture Texture { get; }
		public HeadlessFrameBuffer(Size s) { Texture = new HeadlessTexture { Size = s }; }
		public void Bind() { }
		public void DisableScissor() { }
		public void EnableScissor(Rectangle rect) { }
		public void Unbind() { }
		public void Dispose() { }
	}

	public class HeadlessIndexBuffer : IIndexBuffer
	{
		public void Bind() { }
		public void Dispose() { }
	}

	public class HeadlessShader : IShader
	{
		public void Bind() { }
		public void PrepareRender() { }
		public void SetBool(string name, bool value) { }
		public void SetMatrix(string param, float[] mtx) { }
		public void SetTexture(string param, ITexture texture) { }
		public void SetVec(string name, float x) { }
		public void SetVec(string name, float x, float y) { }
		public void SetVec(string name, float x, float y, float z) { }
		public void SetVec(string name, float[] vec, int length) { }
	}

	public class HeadlessTexture : ITexture
	{
		public Size Size { get; set; }
		public TextureScaleFilter ScaleFilter { get; set; }
		public byte[] GetData() => Array.Empty<byte>();
		public void SetData(byte[] colors, int width, int height) { Size = new Size(width, height); }
		public void SetDataFromReadBuffer(Rectangle rect) { }
		public void SetFloatData(float[] data, int width, int height) { Size = new Size(width, height); }
		public void Dispose() { }
	}

	public class HeadlessVertexBuffer<T> : IVertexBuffer<T> where T : struct
	{
		public void Bind() { }
		public void SetData(T[] vertices, int length) { }
		public void SetData(ref T[] vertices, int length) { }
		public void SetData(T[] vertices, int offset, int start, int length) { }
		public void Dispose() { }
	}

	public class HeadlessFont : IFont
	{
		public FontGlyph CreateGlyph(char c, int size, float deviceScale)
		{
			return new FontGlyph { Offset = int2.Zero, Size = new Size(0, 0), Advance = 0, Data = Array.Empty<byte>() };
		}
		public void Dispose() { }
	}

	sealed class DummySoundEngine : ISoundEngine
	{
		public bool Dummy => true;

		public SoundDevice[] AvailableDevices()
		{
			var defaultDevices = new[]
			{
				new SoundDevice(null, "No Sound Output"),
			};

			return defaultDevices;
		}

		public ISoundSource AddSoundSourceFromMemory(byte[] data, int channels, int sampleBits, int sampleRate)
		{
			return new NullSoundSource();
		}

		public ISound Play2D(ISoundSource soundSource, bool loop, bool relative, WPos pos, float volume, bool attenuateVolume)
		{
			return new NullSound();
		}

		public ISound Play2DStream(Stream stream, int channels, int sampleBits, int sampleRate, bool loop, bool relative, WPos pos, float volume)
		{
			return null;
		}

		public float Volume
		{
			get => 0;
			set { }
		}

		public void PauseSound(ISound sound, bool paused) { }
		public void SetAllSoundsPaused(bool paused) { }
		public void SetSoundVolume(float volume, ISound music, ISound video) { }
		public void StopSound(ISound sound) { }
		public void StopAllSounds() { }
		public void SetListenerPosition(WPos position) { }
		public void SetSoundLooping(bool looping, ISound sound) { }
		public void SetSoundPosition(ISound sound, WPos position) { }
		public void Dispose() { }
	}

	sealed class NullSoundSource : ISoundSource
	{
		public void Dispose() { }
	}

	sealed class NullSound : ISound
	{
		public float Volume { get; set; }
		public float SeekPosition => 0;
		public bool Complete => false;

		public void SetPosition(WPos position) { }
	}
}