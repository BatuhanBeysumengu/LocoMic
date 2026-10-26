using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace EqualizerHelper.Audio;

/// <summary>
/// Passthrough ISampleProvider that can be armed to capture the next N mono
/// (down-mixed) samples flowing through it, for offline analysis. Zero
/// overhead beyond a lock check when not armed.
/// </summary>
public class AnalysisTapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly object _lock = new();

    private float[]? _captureBuffer;
    private int _captureWriteIndex;
    private TaskCompletionSource<float[]>? _captureTcs;

    public AnalysisTapSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public Task<float[]> ArmAndCaptureAsync(int targetMonoSampleCount, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            _captureBuffer = new float[targetMonoSampleCount];
            _captureWriteIndex = 0;
            _captureTcs = tcs;
        }

        cancellationToken.Register(() =>
        {
            lock (_lock)
            {
                if (_captureTcs == tcs)
                {
                    _captureTcs = null;
                    _captureBuffer = null;
                }
            }

            tcs.TrySetCanceled(cancellationToken);
        });

        return tcs.Task;
    }

    public int Read(Span<float> buffer)
    {
        int samplesRead = _source.Read(buffer);

        lock (_lock)
        {
            if (_captureBuffer != null && _captureTcs != null)
            {
                for (int i = 0; i + _channels <= samplesRead && _captureWriteIndex < _captureBuffer.Length; i += _channels)
                {
                    float sum = 0;
                    for (int c = 0; c < _channels; c++)
                    {
                        sum += buffer[i + c];
                    }

                    _captureBuffer[_captureWriteIndex++] = sum / _channels;
                }

                if (_captureWriteIndex >= _captureBuffer.Length)
                {
                    var result = _captureBuffer;
                    var tcs = _captureTcs;
                    _captureBuffer = null;
                    _captureTcs = null;
                    tcs.TrySetResult(result);
                }
            }
        }

        return samplesRead;
    }
}
