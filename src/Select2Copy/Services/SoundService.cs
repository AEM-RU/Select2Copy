using System.IO;
using System.Media;

namespace Select2Copy.Services;

internal static class SoundService
{
    private static readonly object Gate = new();
    private static SoundPlayer? _player;
    private static MemoryStream? _stream;
    private static bool _loaded;

    public static void Play()
    {
        try
        {
            lock (Gate)
            {
                EnsurePlayer();
                _player!.Play();
            }
        }
        catch (Exception ex)
        {
            Log.Write("Sound: " + ex.Message);
            try
            {
                SystemSounds.Asterisk.Play();
            }
            catch
            {
            }
        }
    }

    private static void EnsurePlayer()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            using Stream? resource = typeof(SoundService).Assembly.GetManifestResourceStream("Select2Copy.Assets.copied.wav");
            if (resource == null)
            {
                throw new InvalidOperationException("Sound resource not found");
            }

            _stream = new MemoryStream();
            resource.CopyTo(_stream);
            _stream.Position = 0;
            _player = new SoundPlayer(_stream);
            _player.Load();
        }
        catch
        {
            _player?.Dispose();
            _player = null;
            _stream?.Dispose();
            _stream = null;
            _loaded = false;
            throw;
        }
    }
}
