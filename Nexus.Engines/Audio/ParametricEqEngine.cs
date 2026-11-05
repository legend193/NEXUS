using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Abstractions.Audio;

namespace Nexus.Engines.Audio;

public struct BiQuadCoefficients
{
    public double B0;
    public double B1;
    public double B2;
    public double A0;
    public double A1;
    public double A2;

    public static BiQuadCoefficients CreateIdentity()
    {
        return new BiQuadCoefficients
        {
            B0 = 1.0, B1 = 0.0, B2 = 0.0,
            A0 = 1.0, A1 = 0.0, A2 = 0.0
        };
    }

    public static BiQuadCoefficients Calculate(ParametricFilterType type, double frequency, double gainDb, double qFactor, int sampleRate = 48000)
    {
        frequency = Math.Clamp(frequency, 20.0, sampleRate / 2.0 - 100.0);
        qFactor = Math.Max(0.01, qFactor);

        double w0 = 2.0 * Math.PI * frequency / sampleRate;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double alpha = sinW0 / (2.0 * qFactor);
        double a = Math.Pow(10.0, gainDb / 40.0); // sqrt(10^(gainDb/20))

        double b0 = 1.0, b1 = 0.0, b2 = 0.0, a0 = 1.0, a1 = 0.0, a2 = 0.0;

        switch (type)
        {
            case ParametricFilterType.PeakingEQ:
                b0 = 1.0 + alpha * a;
                b1 = -2.0 * cosW0;
                b2 = 1.0 - alpha * a;
                a0 = 1.0 + alpha / a;
                a1 = -2.0 * cosW0;
                a2 = 1.0 - alpha / a;
                break;

            case ParametricFilterType.LowShelf:
                {
                    double sqrtA2 = 2.0 * Math.Sqrt(a) * alpha;
                    b0 = a * ((a + 1.0) - (a - 1.0) * cosW0 + sqrtA2);
                    b1 = 2.0 * a * ((a - 1.0) - (a + 1.0) * cosW0);
                    b2 = a * ((a + 1.0) - (a - 1.0) * cosW0 - sqrtA2);
                    a0 = (a + 1.0) + (a - 1.0) * cosW0 + sqrtA2;
                    a1 = -2.0 * ((a - 1.0) + (a + 1.0) * cosW0);
                    a2 = (a + 1.0) + (a - 1.0) * cosW0 - sqrtA2;
                }
                break;

            case ParametricFilterType.HighShelf:
                {
                    double sqrtA2 = 2.0 * Math.Sqrt(a) * alpha;
                    b0 = a * ((a + 1.0) + (a - 1.0) * cosW0 + sqrtA2);
                    b1 = -2.0 * a * ((a - 1.0) + (a + 1.0) * cosW0);
                    b2 = a * ((a + 1.0) + (a - 1.0) * cosW0 - sqrtA2);
                    a0 = (a + 1.0) - (a - 1.0) * cosW0 + sqrtA2;
                    a1 = 2.0 * ((a - 1.0) - (a + 1.0) * cosW0);
                    a2 = (a + 1.0) - (a - 1.0) * cosW0 - sqrtA2;
                }
                break;

            case ParametricFilterType.LowPass:
                b0 = (1.0 - cosW0) / 2.0;
                b1 = 1.0 - cosW0;
                b2 = (1.0 - cosW0) / 2.0;
                a0 = 1.0 + alpha;
                a1 = -2.0 * cosW0;
                a2 = 1.0 - alpha;
                break;

            case ParametricFilterType.HighPass:
                b0 = (1.0 + cosW0) / 2.0;
                b1 = -(1.0 + cosW0);
                b2 = (1.0 + cosW0) / 2.0;
                a0 = 1.0 + alpha;
                a1 = -2.0 * cosW0;
                a2 = 1.0 - alpha;
                break;

            case ParametricFilterType.Notch:
                b0 = 1.0;
                b1 = -2.0 * cosW0;
                b2 = 1.0;
                a0 = 1.0 + alpha;
                a1 = -2.0 * cosW0;
                a2 = 1.0 - alpha;
                break;
        }

        // Normalize so a0 = 1.0
        return new BiQuadCoefficients
        {
            B0 = b0 / a0,
            B1 = b1 / a0,
            B2 = b2 / a0,
            A0 = 1.0,
            A1 = a1 / a0,
            A2 = a2 / a0
        };
    }

    public double EvaluateGainDb(double freq, int sampleRate)
    {
        double phi = 2.0 * Math.PI * freq / sampleRate;
        double cos1 = Math.Cos(phi);
        double cos2 = Math.Cos(2.0 * phi);
        double sin1 = Math.Sin(phi);
        double sin2 = Math.Sin(2.0 * phi);

        double numRe = B0 + B1 * cos1 + B2 * cos2;
        double numIm = -(B1 * sin1 + B2 * sin2);

        double denRe = A0 + A1 * cos1 + A2 * cos2;
        double denIm = -(A1 * sin1 + A2 * sin2);

        double numMagSq = numRe * numRe + numIm * numIm;
        double denMagSq = denRe * denRe + denIm * denIm;

        if (denMagSq < 1e-12) return 0.0;
        double magSq = numMagSq / denMagSq;
        return 10.0 * Math.Log10(Math.Max(1e-12, magSq));
    }
}

public class ParametricEqEngine : IParametricEqEngine
{
    private readonly List<ParametricEqBand> _bands = new();
    private readonly object _lock = new();

    public float PreAmpGainDb { get; set; } = 0.0f;
    public IReadOnlyList<ParametricEqBand> Bands { get { lock (_lock) return _bands.ToList(); } }

    public ParametricEqEngine()
    {
        LoadPreset(EqualizerPreset.Gaming);
    }

    public void LoadPreset(EqualizerPreset preset)
    {
        lock (_lock)
        {
            _bands.Clear();
            PreAmpGainDb = preset.PreAmpGainDb;

            if (preset.ParametricBands != null && preset.ParametricBands.Count > 0)
            {
                foreach (var b in preset.ParametricBands)
                {
                    _bands.Add(new ParametricEqBand
                    {
                        BandIndex = b.BandIndex,
                        FilterType = b.FilterType,
                        Frequency = b.Frequency,
                        GainDb = b.GainDb,
                        QFactor = b.QFactor,
                        IsEnabled = b.IsEnabled
                    });
                }
            }
            else
            {
                for (int i = 0; i < 10; i++)
                {
                    float g = i < preset.BandGainsDb.Length ? preset.BandGainsDb[i] : 0f;
                    var type = i switch
                    {
                        0 => ParametricFilterType.LowShelf,
                        9 => ParametricFilterType.HighShelf,
                        _ => ParametricFilterType.PeakingEQ
                    };

                    _bands.Add(new ParametricEqBand
                    {
                        BandIndex = i,
                        FilterType = type,
                        Frequency = EqualizerPreset.DefaultFrequencies[i],
                        GainDb = g,
                        QFactor = 1.414f,
                        IsEnabled = true
                    });
                }
            }
        }
    }

    public void UpdateBand(int bandIndex, float gainDb, float frequency, float qFactor, ParametricFilterType type, bool isEnabled)
    {
        lock (_lock)
        {
            var band = _bands.FirstOrDefault(b => b.BandIndex == bandIndex);
            if (band != null)
            {
                band.GainDb = gainDb;
                band.Frequency = frequency;
                band.QFactor = qFactor;
                band.FilterType = type;
                band.IsEnabled = isEnabled;
            }
        }
    }

    public double[] CalculateFrequencyResponse(double[] frequencies, int sampleRate = 48000)
    {
        var response = new double[frequencies.Length];
        List<BiQuadCoefficients> activeFilters = new();

        lock (_lock)
        {
            foreach (var b in _bands)
            {
                if (b.IsEnabled && Math.Abs(b.GainDb) > 0.01f)
                {
                    activeFilters.Add(BiQuadCoefficients.Calculate(b.FilterType, b.Frequency, b.GainDb, b.QFactor, sampleRate));
                }
            }
        }

        for (int i = 0; i < frequencies.Length; i++)
        {
            double f = frequencies[i];
            double totalGain = PreAmpGainDb;

            foreach (var filter in activeFilters)
            {
                totalGain += filter.EvaluateGainDb(f, sampleRate);
            }

            response[i] = totalGain;
        }

        return response;
    }

    #region Microphone DSP Enhancer
    public static float ApplyNoiseGate(float sample, ref float envelope, MicrophoneDspConfig config, float sampleRate = 48000f)
    {
        if (!config.NoiseGateEnabled) return sample;

        float absSample = Math.Abs(sample);
        float attackAlpha = (float)Math.Exp(-1.0 / (config.NoiseGateAttackMs * 0.001 * sampleRate));
        float releaseAlpha = (float)Math.Exp(-1.0 / (config.NoiseGateReleaseMs * 0.001 * sampleRate));

        if (absSample > envelope)
            envelope = attackAlpha * envelope + (1.0f - attackAlpha) * absSample;
        else
            envelope = releaseAlpha * envelope + (1.0f - releaseAlpha) * absSample;

        float thresholdLinear = (float)Math.Pow(10.0, config.NoiseGateThresholdDb / 20.0);
        if (envelope < thresholdLinear)
        {
            float attenuation = envelope / Math.Max(1e-6f, thresholdLinear);
            return sample * attenuation * attenuation;
        }

        return sample;
    }

    public static float ApplyCompressor(float sample, ref float compEnvelope, MicrophoneDspConfig config, float sampleRate = 48000f)
    {
        if (!config.CompressorEnabled) return sample;

        float absSample = Math.Abs(sample);
        float attackAlpha = (float)Math.Exp(-1.0 / (config.CompressorAttackMs * 0.001 * sampleRate));
        float releaseAlpha = (float)Math.Exp(-1.0 / (config.CompressorReleaseMs * 0.001 * sampleRate));

        if (absSample > compEnvelope)
            compEnvelope = attackAlpha * compEnvelope + (1.0f - attackAlpha) * absSample;
        else
            compEnvelope = releaseAlpha * compEnvelope + (1.0f - releaseAlpha) * absSample;

        float envDb = 20.0f * (float)Math.Log10(Math.Max(1e-6f, compEnvelope));
        float gainReductionDb = 0f;

        if (envDb > config.CompressorThresholdDb)
        {
            float overDb = envDb - config.CompressorThresholdDb;
            gainReductionDb = overDb * (1.0f - 1.0f / Math.Max(1.0f, config.CompressorRatio));
        }

        float totalGainDb = -gainReductionDb + config.CompressorMakeUpGainDb;
        float linearGain = (float)Math.Pow(10.0, totalGainDb / 20.0);
        float processed = sample * linearGain;

        if (config.LimiterEnabled)
        {
            float ceilingLinear = (float)Math.Pow(10.0, config.LimiterCeilingDb / 20.0);
            processed = Math.Clamp(processed, -ceilingLinear, ceilingLinear);
        }

        return processed;
    }
    #endregion
}
